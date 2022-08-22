// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Health;
using System.Fabric.Query;
using System.Linq;
using Microsoft.ServiceFabric.Services.Communication;
using Yarp.ReverseProxy.Configuration;
using Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Util.Tests
{
    /// <summary>
    /// Factory helper class for tests related to Service Fabric integration.
    /// </summary>
    internal static class SFTestHelpers
    {
        // Factory
        internal static ApplicationWrapper FakeApp(string appTypeName, string appTypeVersion = "1.2.3")
        {
            return new ApplicationWrapper
            {
                ApplicationName = new ApplicationNameKey(new Uri($"fabric:/{appTypeName}")),
                ApplicationTypeName = new ApplicationTypeNameKey($"{appTypeName}Type"),
                ApplicationTypeVersion = new ApplicationTypeVersionKey(appTypeVersion),
            };
        }
        internal static ServiceWrapper FakeService(Uri serviceName, string serviceTypeName, string serviceManifestVersion = "2.3.4", ServiceKind serviceKind = ServiceKind.Stateless)
        {
            return new ServiceWrapper
            {
                ServiceName = new ServiceNameKey(serviceName),
                ServiceTypeName = new ServiceTypeNameKey(serviceTypeName),
                ServiceManifestVersion = serviceManifestVersion,
                ServiceKind = serviceKind,
            };
        }
        internal static PartitionWrapper FakePartition(string guid)
        {
            return new PartitionWrapper
            {
                PartitionId = string.IsNullOrEmpty(guid) ? Guid.NewGuid() : new Guid(guid),

                // PartitionId = new Guid(guid),
            };
        }
        internal static ReplicaWrapper FakeReplica(Uri serviceName, long id)
        {
            var address = $"https://127.0.0.1/{serviceName.Authority}/{id}";
            return new ReplicaWrapper
            {
                Id = id,
                ReplicaAddress = $"{{'Endpoints': {{'': '{address}' }} }}".Replace("'", "\""),
                HealthState = HealthState.Ok,
                ReplicaStatus = ServiceReplicaStatus.Ready,

                Role = ReplicaRole.None,
                ServiceKind = ServiceKind.Stateless,
            };
        }

        internal static Dictionary<string, string> DummyLabels(string backendId, bool enableGateway = true, bool activeHealthChecks = false)
        {
            return new Dictionary<string, string>()
        {
            { "Yarp.Enable", enableGateway ? "true" : "false" },
            { "Yarp.Backend.BackendId", backendId },
            { "Yarp.Backend.HealthCheck.Active.Enabled", activeHealthChecks ? "true" : "false" },
            { "Yarp.Backend.HealthCheck.Active.Interval", "00:00:5" },
            { "Yarp.Backend.HealthCheck.Active.Timeout", "00:00:5" },

            // { "Yarp.Backend.HealthCheck.Active.Port", "8787" },
            { "Yarp.Backend.HealthCheck.Active.Path", "/api/health" },
            { "Yarp.Backend.HealthCheck.Active.Policy", "ConsecutiveFailures" },
            { "Yarp.Backend.Metadata.Foo", "Bar" },
            { "Yarp.Routes.MyRoute.Metadata.Foo", "Bar" },
            { "Yarp.Routes.MyRoute.Hosts", "example.com" },
            { "Yarp.Routes.MyRoute.Order", "2" },
        };
        }

        /// <summary>
        /// Build a <see cref="DestinationConfig" /> from a Service Fabric <see cref="ReplicaWrapper" />.
        /// </summary>
        /// <remarks>
        /// The address JSON of the replica is expected to have exactly one endpoint, and that one will be used.
        /// </remarks>
        internal static KeyValuePair<string, DestinationConfig> BuildDestinationFromReplicaAndPartition(ReplicaWrapper replica, PartitionWrapper partition, string healthListenerName = null)
        {
            ServiceEndpointCollection.TryParseEndpointsString(replica.ReplicaAddress, out var endpoints);
            endpoints.TryGetFirstEndpointAddress(out var address);

            string healthAddressUri = null;
            if (healthListenerName != null)
            {
                endpoints.TryGetEndpointAddress(healthListenerName, out healthAddressUri);
            }

            var destinationId = $"{partition.PartitionId}/{replica.Id}";

            return KeyValuePair.Create(
                destinationId,
                new DestinationConfig
                {
                    Address = address,
                    Health = healthAddressUri,
                    Metadata = new Dictionary<string, string>
                    {
                        { "__SF.PartitionId", partition.PartitionId.ToString() ?? string.Empty },
                        { "__SF.ReplicaId", replica.Id.ToString() ?? string.Empty },
                    },
                });
        }

        // Mocking helpers
        internal static ApplicationWrapper CreateApp_1Service_SingletonPartition_1Replica(
            string appTypeName,
            string serviceTypeName,
            out ServiceWrapper service,
            out ReplicaWrapper replica,
            out PartitionWrapper partition,
            ServiceKind serviceKind = ServiceKind.Stateless)
        {
            service = CreateService(appTypeName, serviceTypeName, 1, 1, out var replicas, out var partitions, serviceKind);
            replica = replicas[0];
            partition = partitions[0];

            return SFTestHelpers.FakeApp(appTypeName, appTypeName);
        }
        internal static ApplicationWrapper CreateApp_1StatefulService_2Partition_1ReplicasEach(
            string appTypeName,
            string serviceTypeName,
            out ServiceWrapper service,
            out List<ReplicaWrapper> replicas,
            out List<PartitionWrapper> partitions,
            ServiceKind serviceKind = ServiceKind.Stateful)
        {
            service = CreateService(appTypeName, serviceTypeName, 2, 1, out replicas, out partitions);

            return SFTestHelpers.FakeApp(appTypeName, appTypeName);
        }
        internal static ApplicationWrapper CreateApp_1StatelfulService_2Partition_2ReplicasEach(
            string appTypeName,
            string appTypeVersion,
            string serviceTypeName,
            out ServiceWrapper service,
            out List<ReplicaWrapper> replicas,
            out List<PartitionWrapper> partitions,
            ServiceKind serviceKind = ServiceKind.Stateful)
        {
            service = CreateService(appTypeName, serviceTypeName, 2, 2, out replicas, out partitions, serviceKind);

            return SFTestHelpers.FakeApp(appTypeName, appTypeVersion);
        }
        internal static ApplicationWrapper CreateApp_2StatelessService_SingletonPartition_1Replica(
            string appTypeName,
            string appTypeVersion,
            string serviceTypeName1,
            string serviceTypeName2,
            out ServiceWrapper service1,
            out ServiceWrapper service2,
            out ReplicaWrapper service1replica,
            out ReplicaWrapper service2replica,
            out PartitionWrapper service1partition,
            out PartitionWrapper service2partition)
        {
            service1 = CreateService(appTypeName, serviceTypeName1, 1, 1, out var replicas1, out var partitions1);
            service2 = CreateService(appTypeName, serviceTypeName2, 1, 1, out var replicas2, out var partitions2);
            service1replica = replicas1[0];
            service2replica = replicas2[0];
            service1partition = partitions1[0];
            service2partition = partitions2[0];

            return SFTestHelpers.FakeApp(appTypeName, appTypeVersion);
        }
        internal static ServiceWrapper CreateService(string appName, string serviceName, int numPartitions, int numReplicasPerPartition, out List<ReplicaWrapper> replicas, out List<PartitionWrapper> partitions, ServiceKind serviceKind = ServiceKind.Stateless)
        {
            var svcName = new Uri($"fabric:/{appName}/{serviceName}");
            var service = SFTestHelpers.FakeService(svcName, $"{serviceName}Type", serviceKind: serviceKind);
            replicas = new List<ReplicaWrapper>();
            partitions = new List<PartitionWrapper>();

            for (var i = 0; i < numPartitions; i++)
            {
                var partitionReplicas = Enumerable.Range(i * numReplicasPerPartition, numReplicasPerPartition).Select(replicaId => SFTestHelpers.FakeReplica(svcName, replicaId)).ToList();
                replicas.AddRange(partitionReplicas);
                var partition = SFTestHelpers.FakePartition(null);
                partitions.Add(partition);
            }
            return service;
        }

        internal static List<ClusterConfig> ClusterWithDestinations(
            SFYarpBackendService backendService,
            params KeyValuePair<string, DestinationConfig>[] destinations)
        {
            var partitionDestinations = new Dictionary<string, Dictionary<string, DestinationConfig>>();
            foreach (var partition in backendService.FabricService.Partitions)
            {
                var newDestinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);
                foreach (var destination in destinations)
                {
                    if (destination.Key.Contains(partition.Partition.PartitionId.ToString()))
                    {
                        newDestinations.Add(destination.Key, destination.Value);
                    }
                }
                partitionDestinations.Add(partition.Partition.PartitionId.ToString(), newDestinations);
            }
            List<string> errors = new List<string>();

            return LabelsParserV2.BuildClusters(backendService, partitionDestinations, errors);
        }
    }
}
