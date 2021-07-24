// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.Common.Abstractions.Time;
using IslandGateway.Common.Util;
using IslandGateway.FabricDiscovery.FabricWrapper;
using IslandGateway.FabricDiscovery.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace IslandGateway.FabricDiscovery.IslandGatewayConfig
{
    /// <summary>
    /// Manages discovery of Service Fabric Properties for services that have opted-in
    /// via label <c>IslandGateway.EnableDynamicOverrides</c>, and computes the final Island Gateway configs.
    /// The processing sequence is
    ///   * <see cref="Topology.TopologyDiscoveryWorker"/>, then
    ///   * <see cref="IslandGatewayTopologyMapperWorker"/>, then
    ///   * this
    ///   * <see cref="IslandGatewayConfigSerializerWorker"/>.
    /// </summary>
    internal class IslandGatewayConfigProducerWorker : CriticalBackgroundService, IProxyConfigProvider
    {
        private static readonly TimeSpan DefaultPropertyPollGranularity = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DefaultPropertyUpdateInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DefaultCoolDownPropertyUpdateInterval = TimeSpan.FromSeconds(20);

        private readonly ISnapshotProvider<IReadOnlyList<IslandGatewayBackendService>> igwTopologyProvider;
        private readonly IPropertyManagementClientWrapper propertyClient;
        private readonly IMonotonicTimer timer;
        private readonly IIslandGatewayConfigProducer configProducer;
        private readonly IslandGatewayFreshnessTracker freshnessTracker;
        private readonly FabricDiscoveryOptions options;
        private readonly ILogger<IslandGatewayConfigProducerWorker> logger;
        private readonly IOperationLogger operationLogger;
        private readonly RecurringTask recurringTask;

        private Snapshot<IReadOnlyList<IslandGatewayBackendService>> currentIgwTopology;
        private TimeSpan? nextPropertiesFetch;

        private IProxyConfig snapshot;
        private CancellationTokenSource changeToken;

        public IslandGatewayConfigProducerWorker(
            ISnapshotProvider<IReadOnlyList<IslandGatewayBackendService>> igwTopologyProvider,
            IPropertyManagementClientWrapper propertyClient,
            IMonotonicTimer timer,
            IIslandGatewayConfigProducer configProducer,
            IslandGatewayFreshnessTracker freshnessTracker,
            IProcessExiter processExiter,
            IOptions<FabricDiscoveryOptions> options,
            ILogger<IslandGatewayConfigProducerWorker> logger,
            IOperationLogger operationLogger)
            : base(CreateWorkerOptions(options), processExiter, logger)
        {
            this.igwTopologyProvider = igwTopologyProvider ?? throw new ArgumentNullException(nameof(igwTopologyProvider));
            this.propertyClient = propertyClient ?? throw new ArgumentNullException(nameof(propertyClient));
            this.timer = timer ?? throw new ArgumentNullException(nameof(timer));
            this.configProducer = configProducer ?? throw new ArgumentNullException(nameof(configProducer));
            this.freshnessTracker = freshnessTracker ?? throw new ArgumentNullException(nameof(freshnessTracker));
            this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
            this.recurringTask = new RecurringTask(this.RunIterationAsync)
                .WithLogging(logger, operationLogger, "IslandGatewayConfigProducerWorker.Iteration")
                .WithAbortOnFailure() // This worker should never fail, a single failure is grounds to abort
                .WithIterationTimeout(options.Value.AbortAfterTimeoutInSeconds > 0 ? TimeSpan.FromSeconds(options.Value.AbortAfterTimeoutInSeconds) : null);
        }

        public IProxyConfig GetConfig() => this.snapshot;

        protected override async Task InitAsync(CancellationToken cancellation)
        {
            this.currentIgwTopology = this.igwTopologyProvider.GetSnapshot();
            if (this.currentIgwTopology == null)
            {
                throw new InvalidOperationException($"Island Gateway topology should already be known when {nameof(IslandGatewayConfigProducerWorker)} starts.");
            }

            (_, this.nextPropertiesFetch) = await this.UpdateAllPropertiesAsync(this.currentIgwTopology.Value, cancellation);
            var (clusters, routes) = this.configProducer.ProduceConfig(this.currentIgwTopology.Value);
            this.UpdateSnapshot(clusters, routes);
        }

        protected override Task RunAsync(CancellationToken cancellation) => this.recurringTask.RunAsync(cancellation);

        private static WorkerOptions CreateWorkerOptions(IOptions<FabricDiscoveryOptions> options)
        {
            _ = options?.Value ?? throw new ArgumentNullException(nameof(options));
            return new WorkerOptions
            {
                InitializationTimeout = options.Value.AbortAfterTimeoutInSeconds > 0 ? TimeSpan.FromSeconds(options.Value.AbortAfterTimeoutInSeconds) : null,
            };
        }

        /// <summary>
        /// Executes one iteration which involves scanning through all services, fetching properties for those that are due,
        /// and computing the YARP model for each service.
        /// </summary>
        private async Task RunIterationAsync(CancellationToken cancellation)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (this.currentIgwTopology.ChangeToken.RegisterChangeCallback(static state => ((CancellationTokenSource)state).Cancel(), cts))
            {
                try
                {
                    // Stop the freshness counter while we wait for the next cycle.
                    // We are considered fully up to date with properties when there is nothing to update.
                    this.freshnessTracker.Properties.Reset();

                    if (this.nextPropertiesFetch.HasValue)
                    {
                        var delay = this.nextPropertiesFetch.Value + DefaultPropertyPollGranularity - this.timer.CurrentTime;
                        if (delay.Ticks > 0)
                        {
                            this.logger.LogInformation($"No properties to fetch for {delay.TotalSeconds:F1} ms or until topology changes.");
                            await this.timer.Delay(delay, cts.Token);
                        }
                    }
                    else
                    {
                        this.logger.LogInformation($"No properties need to be fetched, delaying until topology changes.");
                        await Task.Delay(Timeout.Infinite, cts.Token);
                    }
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    // Happy case, topology just changed.
                }
            }

            // When we get here, it's either because the topology has just changed or it's time to re-check properties.
            this.freshnessTracker.Properties.SetFresh();
            var newTopology = this.igwTopologyProvider.GetSnapshot();
            bool topologyChanged = !object.ReferenceEquals(this.currentIgwTopology, newTopology);

            bool propertiesChanged;
            (propertiesChanged, this.nextPropertiesFetch) = await this.UpdateAllPropertiesAsync(newTopology.Value, cancellation);

            if (topologyChanged || propertiesChanged)
            {
                var (clusters, routes) = this.configProducer.ProduceConfig(newTopology.Value);
                this.UpdateSnapshot(clusters, routes);

                // Only update the current topology at the end -- if anything fails before we get here, we want the next iteration to retry
                // (otherwise it might sit doing nothing until the next SF topology change.
                this.currentIgwTopology = newTopology;
            }
        }

        private Task<(bool PropertiesChanged, TimeSpan? NextPropertiesFetch)> UpdateAllPropertiesAsync(IReadOnlyList<IslandGatewayBackendService> backendServices, CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteAsync(
                "IslandGatewayConfigProducerWorker.UpdateAllPropertiesAsync",
                async () =>
                {
                    TimeSpan? nextPropertiesFetch = null;
                    bool changed = false;
                    int servicesChecked = 0;
                    int servicesChanged = 0;
                    int servicesFailed = 0;
                    foreach (var service in backendServices)
                    {
                        if (service.EffectiveLabels == null ||
                            !LabelsParserV2.UseDynamicOverrides(service.EffectiveLabels))
                        {
                            // No need to fetch properties for this service
                            if (service.LabelOverrides != null)
                            {
                                this.logger.LogInformation($"Service '{service.FabricService.Service.ServiceName}' is no longer using dynamic property overrides.");
                                service.LabelOverrides = null;
                                service.FinalEffectiveLabels = service.EffectiveLabels;
                                service.NextPropertiesFetch = null;
                                changed = true;
                            }

                            continue;
                        }

                        var now = this.timer.CurrentTime;
                        if (service.NextPropertiesFetch > now)
                        {
                            // It isn't time to re-fetch this service yet, life's good...
                            continue;
                        }

                        servicesChecked++;
                        bool succeeded = false;
                        try
                        {
                            if (await this.FetchProperties(service, cancellation))
                            {
                                servicesChanged++;
                                changed = true;
                            }

                            succeeded = true;
                        }
                        catch
                        {
                            // FetchProperties already logged the error in this case, so we can safely swallow.
                            servicesFailed++;
                        }

                        // if the fetch failed, use a larger interval for the next attempt -- we won't hammer Service Fabric for this service immediately after.
                        service.NextPropertiesFetch = now + (succeeded ? DefaultPropertyUpdateInterval : DefaultCoolDownPropertyUpdateInterval);

                        // Keep track of when is the soonest we may have to re-check for property changes
                        if (!nextPropertiesFetch.HasValue || service.NextPropertiesFetch.Value < nextPropertiesFetch.Value)
                        {
                            nextPropertiesFetch = service.NextPropertiesFetch;
                        }

                        var operationContext = this.operationLogger.Context;
                        operationContext.SetProperty("totalServices", backendServices.Count.ToString());
                        operationContext.SetProperty(nameof(servicesChecked), servicesChecked.ToString());
                        operationContext.SetProperty(nameof(servicesChanged), servicesChanged.ToString());
                        operationContext.SetProperty(nameof(servicesFailed), servicesFailed.ToString());
                    }

                    return (changed, nextPropertiesFetch);
                });
        }

        private Task<bool> FetchProperties(IslandGatewayBackendService service, CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteAsync(
                "IslandGatewayConfigProducerWorker.FetchProperties",
                async () =>
                {
                    this.operationLogger.Context.SetProperty(nameof(service.FabricService.Service.ServiceName), service.FabricService.Service.ServiceName.ToString());
                    var oldProps = service.LabelOverrides;

                    Dictionary<string, string> newProps;
                    try
                    {
                        newProps = await this.propertyClient.EnumeratePropertiesAsync(service.FabricService.Service.ServiceName, Consts.DefaultFabricApiTimeout, cancellation);
                    }
                    catch (FabricElementNotFoundException)
                    {
                        newProps = new Dictionary<string, string>(StringComparer.Ordinal);
                    }

                    bool changed = oldProps == null || !DictionaryEqualityUtil.AreDictionariesEqual(oldProps, newProps);
                    if (changed)
                    {
                        service.LabelOverrides = newProps;
                        service.FinalEffectiveLabels = DictionaryUtil.CombineDictionaries(service.EffectiveLabels, service.LabelOverrides, StringComparer.Ordinal);
                    }

                    this.operationLogger.Context.SetProperty("changed", changed.ToString());
                    return changed;
                });
        }

        private void UpdateSnapshot(List<ClusterConfig> clusters, List<RouteConfig> routes)
        {
            this.logger.LogInformation($"Signaling new Island Gateway configs. {clusters.Count} clusters, {routes.Count} routes.");

            using var oldToken = this.changeToken;
            this.changeToken = new CancellationTokenSource();
            this.snapshot = new IslandGatewayConfigSnapshot
            {
                Clusters = clusters,
                Routes = routes,
                ChangeToken = new CancellationChangeToken(this.changeToken.Token),
            };

            try
            {
                oldToken?.Cancel(throwOnFirstException: false);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error signaling change.");
            }
        }

        private class IslandGatewayConfigSnapshot : IProxyConfig
        {
            public List<RouteConfig> Routes { get; internal set; } = new List<RouteConfig>();

            public List<ClusterConfig> Clusters { get; internal set; } = new List<ClusterConfig>();

            IReadOnlyList<RouteConfig> IProxyConfig.Routes => this.Routes;

            IReadOnlyList<ClusterConfig> IProxyConfig.Clusters => this.Clusters;

            // This field is required.
            public IChangeToken ChangeToken { get; internal set; } = default!;
        }
    }
}
