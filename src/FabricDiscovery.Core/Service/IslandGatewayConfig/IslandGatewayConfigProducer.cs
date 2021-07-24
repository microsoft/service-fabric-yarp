﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Query;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.FabricDiscovery.Topology;
using IslandGateway.FabricDiscovery.Util;
using IslandGateway.ServiceFabricIntegration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceFabric.Services.Communication;
using Yarp.ReverseProxy.Configuration;

namespace IslandGateway.FabricDiscovery.IslandGatewayConfig
{
    internal class IslandGatewayConfigProducer : IIslandGatewayConfigProducer
    {
        private readonly FabricDiscoveryOptions options;
        private readonly ILogger<IslandGatewayConfigProducer> logger;
        private readonly IOperationLogger operationLogger;

        public IslandGatewayConfigProducer(
            IOptions<FabricDiscoveryOptions> options,
            ILogger<IslandGatewayConfigProducer> logger,
            IOperationLogger operationLogger)
        {
            this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
        }

        /// <inheritdoc/>
        public (List<ClusterConfig> Clusters, List<RouteConfig> Routes) ProduceConfig(IReadOnlyList<IslandGatewayBackendService> backendServices)
        {
            _ = backendServices ?? throw new ArgumentNullException(nameof(backendServices));

            return this.operationLogger.Execute(
                "IslandGatewayConfigProducer.ProduceConfig",
                () =>
                {
                    var yarpClusters = new List<ClusterConfig>();
                    var yarpRoutes = new List<RouteConfig>();

                    foreach (var service in backendServices)
                    {
                        var errors = new List<string>();
                        var result = this.ComputeYarpModel(service, errors);

                        if (result != null)
                        {
                            yarpClusters.AddRange(result.Value.Clusters);
                            yarpRoutes.AddRange(result.Value.Routes);
                        }
                    }

                    return (yarpClusters, yarpRoutes);
                });
        }

        private (List<ClusterConfig> Clusters, List<RouteConfig> Routes)? ComputeYarpModel(IslandGatewayBackendService service, List<string> errors)
        {
            if (!LabelsParserV2.IsEnabled(service.FinalEffectiveLabels))
            {
                return null;
            }

            var clusters = this.BuildClusters(service, errors);
            var routes = LabelsParserV2.BuildRoutes(service.FabricService.Service.ServiceName, service.FinalEffectiveLabels, errors);

            if (clusters == null || routes == null)
            {
                return null;
            }

            return (clusters, routes);
        }

        private List<ClusterConfig> BuildClusters(IslandGatewayBackendService service, List<string> errors)
        {
            var clusters = LabelsParserV2.BuildClustersWithoutDestinations(service.FabricApplication.Application, service.FabricService.Service, service.FinalEffectiveLabels, errors);
            if (clusters != null)
            {
                if (clusters.Count != 1)
                {
                    throw new NotSupportedException("We only support one YARP cluster per SF service at the moment.");
                }

                var cluster = clusters[0];
                string defaultListenerName = service.FinalEffectiveLabels.GetValueOrDefault("IslandGateway.Backend.ServiceFabric.ListenerName", string.Empty);
                var destinations = this.BuildDestinations(service.FabricService, service.FinalEffectiveLabels, defaultListenerName, errors);
                int i = 0;
                foreach (var destination in destinations)
                {
                    // TODO: davidni SF-YARP: Fix this mess
                    ((Dictionary<string, DestinationConfig>)cluster.Destinations).Add((i++).ToString(), destination);
                }
            }

            return clusters;
        }

        private List<DestinationConfig> BuildDestinations(DiscoveredServiceEx service, IReadOnlyDictionary<string, string> effectiveLabels, string listenerName, List<string> errors)
        {
            var destinations = new List<DestinationConfig>();

            string healthListenerName = effectiveLabels.GetValueOrDefault("IslandGateway.Backend.Healthcheck.ServiceFabric.ListenerName", string.Empty);
            var statefulReplicaSelectionMode = this.ParseStatefulReplicaSelectionMode(effectiveLabels);
            foreach (var partition in service.Partitions)
            {
                foreach (var replica in partition.Replicas)
                {
                    if (!IsHealthyReplica(replica.Replica))
                    {
                        this.logger.LogInformation($"Skipping unhealthy replica '{replica.Replica.Id}' from partition '{partition.Partition.PartitionId}', service '{service.Service.ServiceName}': ReplicaStatus={replica.Replica.ReplicaStatus}, HealthState={replica.Replica.HealthState}.");
                        continue;
                    }

                    // If service is stateful, we need to determine which replica should we route to (e.g Primary, Secondary, All).
                    if (!IsReplicaEligible(replica.Replica, statefulReplicaSelectionMode))
                    {
                        // Skip this endpoint.
                        this.logger.LogInformation($"Skipping ineligible endpoint '{replica.Replica.Id}' of service '{service.Service.ServiceName}'. {nameof(statefulReplicaSelectionMode)}: {statefulReplicaSelectionMode}.");
                        continue;
                    }

                    static bool IsHealthyReplica(ReplicaWrapper replica)
                    {
                        // Future: Should we only consider replicas that Service Fabric reports as healthy (`replica.HealthState != HealthState.Error`)?
                        // That is precisely what Traefik does, see: https://github.com/containous/traefik-extra-service-fabric/blob/a5c54b8d5409be7aa21b06d55cf186ee4cc25a13/servicefabric.go#L219
                        // It seems misguided in our case, however, since we have an active health probing model
                        // that can determine endpoint health more reliably. In particular because Service Fabric "Error" states does not necessarily mean
                        // that the replica is unavailable, rather only that something in the cluster issued an "Error" report against it.
                        // Skipping the replica here because we *suspect* it might be unavailable could lead to snowball cascading failures.
                        return replica.ReplicaStatus == ServiceReplicaStatus.Ready;
                    }

                    static bool IsReplicaEligible(ReplicaWrapper replica, StatefulReplicaSelectionMode statefulReplicaSelectionMode)
                    {
                        if (replica.ServiceKind != ServiceKind.Stateful)
                        {
                            // Stateless service replicas are always eligible
                            return true;
                        }

                        switch (statefulReplicaSelectionMode)
                        {
                            case StatefulReplicaSelectionMode.Primary:
                                return replica.Role == ReplicaRole.Primary;
                            case StatefulReplicaSelectionMode.ActiveSecondary:
                                return replica.Role == ReplicaRole.ActiveSecondary;
                            case StatefulReplicaSelectionMode.All:
                            default:
                                return true;
                        }
                    }

                    try
                    {
                        var destinationResult = this.BuildDestination(replica.Replica, listenerName, healthListenerName, this.options);
                        if (destinationResult.IsSuccess)
                        {
                            destinations.Add(destinationResult.Value);
                        }
                        else
                        {
                            errors.Add(destinationResult.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Not the user's problem
                        string error = $"Internal error while building destination for replica {replica.Replica.Id} of service {service.Service.ServiceName}.";
                        errors.Add(error);
                        this.logger.LogError(ex, error);
                    }
                }
            }

            return destinations;
        }

        private Result<DestinationConfig, string> BuildDestination(ReplicaWrapper replica, string listenerName, string healthListenerName, FabricDiscoveryOptions options)
        {
            if (!ServiceEndpointCollection.TryParseEndpointsString(replica.ReplicaAddress, out var serviceEndpointCollection))
            {
                return Result<DestinationConfig, string>.Failure($"Could not parse endpoints for replica {replica.Id}.");
            }

            var serviceEndpoint = new FabricServiceEndpoint(
                listenerNames: new[] { listenerName },
                allowedSchemePredicate: scheme => HttpsSchemeSelector(scheme, options),
                emptyStringMatchesAnyListener: true);
            if (!FabricServiceEndpointSelector.TryGetEndpoint(serviceEndpoint, serviceEndpointCollection, out Uri endpointUri))
            {
                return Result<DestinationConfig, string>.Failure($"No acceptable endpoints found for replica '{replica.Id}'. Search criteria: listenerName='{listenerName}', emptyStringMatchesAnyListener=true.");
            }

            // Get service endpoint from the health listener, health listener is optional.
            Uri healthEndpointUri = null;
            if (!string.IsNullOrEmpty(healthListenerName))
            {
                var healthEndpoint = new FabricServiceEndpoint(
                    listenerNames: new[] { healthListenerName },
                    allowedSchemePredicate: scheme => HttpsSchemeSelector(scheme, options),
                    emptyStringMatchesAnyListener: true);
                if (!FabricServiceEndpointSelector.TryGetEndpoint(healthEndpoint, serviceEndpointCollection, out healthEndpointUri))
                {
                    return Result<DestinationConfig, string>.Failure($"No acceptable health endpoints found for replica '{replica.Id}'. Search criteria: listenerName='{healthListenerName}', emptyStringMatchesAnyListener=true.");
                }
            }

            var destination = new DestinationConfig
            {
                Address = endpointUri.ToString(),
                Health = healthEndpointUri?.ToString(),
                Metadata = null,
            };
            return Result<DestinationConfig, string>.Success(destination);

            static bool HttpsSchemeSelector(string urlScheme, FabricDiscoveryOptions options)
            {
                if (options.AllowInsecureHttp)
                {
                    return urlScheme == "https" || urlScheme == "http";
                }
                return urlScheme == "https";
            }
        }

        private StatefulReplicaSelectionMode ParseStatefulReplicaSelectionMode(IReadOnlyDictionary<string, string> serviceExtensionLabels)
        {
            // Parse the value for StatefulReplicaSelectionMode: case insensitive, and trim the white space.
            var statefulReplicaSelectionMode = serviceExtensionLabels.GetValueOrDefault("IslandGateway.Backend.ServiceFabric.StatefulReplicaSelectionMode", StatefulReplicaSelectionLabel.All).Trim();
            if (string.Equals(statefulReplicaSelectionMode, StatefulReplicaSelectionLabel.PrimaryOnly, StringComparison.OrdinalIgnoreCase))
            {
                return StatefulReplicaSelectionMode.Primary;
            }

            if (string.Equals(statefulReplicaSelectionMode, StatefulReplicaSelectionLabel.SecondaryOnly, StringComparison.OrdinalIgnoreCase))
            {
                return StatefulReplicaSelectionMode.ActiveSecondary;
            }

            if (string.Equals(statefulReplicaSelectionMode, StatefulReplicaSelectionLabel.All, StringComparison.OrdinalIgnoreCase))
            {
                return StatefulReplicaSelectionMode.All;
            }

            this.logger.LogWarning($"Invalid replica selection mode: {statefulReplicaSelectionMode}, fallback to selection mode: All.");
            return StatefulReplicaSelectionMode.All;
        }
    }
}
