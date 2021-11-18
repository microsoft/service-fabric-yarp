// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ServiceFabric.Common;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Abstractions.Time;
using Yarp.ServiceFabric.Common.Util;
using Yarp.ServiceFabric.FabricDiscovery.FabricWrapper;
using Yarp.ServiceFabric.FabricDiscovery.Util;

namespace Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig
{
    /// <summary>
    /// Manages discovery of Service Fabric Properties for services that have opted-in
    /// via label <c>Yarp.EnableDynamicOverrides</c>, and computes the final Island Gateway configs.
    /// The processing sequence is
    ///   * <see cref="Topology.TopologyDiscoveryWorker"/>, then
    ///   * <see cref="SFYarpTopologyMapperWorker"/>, then
    ///   * this
    ///   * <see cref="SFYarpConfigSerializerWorker"/>.
    /// </summary>
    internal class SFYarpConfigProducerWorker : CriticalBackgroundService, IProxyConfigProvider
    {
        private static readonly TimeSpan DefaultPropertyPollGranularity = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DefaultPropertyUpdateInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DefaultCoolDownPropertyUpdateInterval = TimeSpan.FromSeconds(20);

        private readonly ISnapshotProvider<IReadOnlyList<SFYarpBackendService>> igwTopologyProvider;
        private readonly IPropertyManagementClientWrapper propertyClient;
        private readonly IMonotonicTimer timer;
        private readonly ISFYarpConfigProducer configProducer;
        private readonly SFYarpFreshnessTracker freshnessTracker;
        private readonly FabricDiscoveryOptions options;
        private readonly ILogger<SFYarpConfigProducerWorker> logger;
        private readonly IOperationLogger operationLogger;
        private readonly RecurringTask recurringTask;

        private Snapshot<IReadOnlyList<SFYarpBackendService>> currentIgwTopology;
        private TimeSpan? nextPropertiesFetch;

        private IProxyConfig snapshot;
        private CancellationTokenSource changeToken;

        public SFYarpConfigProducerWorker(
            ISnapshotProvider<IReadOnlyList<SFYarpBackendService>> igwTopologyProvider,
            IPropertyManagementClientWrapper propertyClient,
            IMonotonicTimer timer,
            ISFYarpConfigProducer configProducer,
            SFYarpFreshnessTracker freshnessTracker,
            IProcessExiter processExiter,
            IOptions<FabricDiscoveryOptions> options,
            ILogger<SFYarpConfigProducerWorker> logger,
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
                .WithLogging(logger, operationLogger, "SFYarpConfigProducerWorker.Iteration")
                .WithAbortOnFailure() // This worker should never fail, a single failure is grounds to abort
                .WithIterationTimeout(options.Value.AbortAfterTimeoutInSeconds > 0 ? TimeSpan.FromSeconds(options.Value.AbortAfterTimeoutInSeconds) : null);
        }

        public IProxyConfig GetConfig() => this.snapshot;

        protected override async Task InitAsync(CancellationToken cancellation)
        {
            this.currentIgwTopology = this.igwTopologyProvider.GetSnapshot();
            if (this.currentIgwTopology == null)
            {
                throw new InvalidOperationException($"Island Gateway topology should already be known when {nameof(SFYarpConfigProducerWorker)} starts.");
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

        private Task<(bool PropertiesChanged, TimeSpan? NextPropertiesFetch)> UpdateAllPropertiesAsync(IReadOnlyList<SFYarpBackendService> backendServices, CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteAsync(
                "SFYarpConfigProducerWorker.UpdateAllPropertiesAsync",
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

        private Task<bool> FetchProperties(SFYarpBackendService service, CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteAsync(
                "SFYarpConfigProducerWorker.FetchProperties",
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
            this.snapshot = new SFYarpConfigSnapshot
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

        private class SFYarpConfigSnapshot : IProxyConfig
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
