// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Yarp.ServiceFabric.Common;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Abstractions.Time;
using Yarp.ServiceFabric.Common.Util;
using Yarp.ServiceFabric.FabricDiscovery.Topology;
using Yarp.ServiceFabric.FabricDiscovery.Util;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.IslandGatewayConfig
{
    /// <summary>
    /// Hosted service responsible for producing Island Gateway abstractions from the Service Fabric topology,
    /// as an intermediary step before additional processing steps performed specifically for Island Gateway.
    /// The processing sequence is
    ///   * <see cref="TopologyDiscoveryWorker"/>, then
    ///   * this, then
    ///   * <see cref="IslandGatewayConfigProducerWorker"/>
    ///   * <see cref="IslandGatewayConfigSerializerWorker"/>.
    /// </summary>
    internal class IslandGatewayTopologyMapperWorker : CriticalBackgroundService, ISnapshotProvider<IReadOnlyList<IslandGatewayBackendService>>
    {
        /// <summary>
        /// Cache parsed Service Types even if they weren't used in an iteration. They may still be used in the future,
        /// e.g. if a new service of a known Service Type is created.
        /// </summary>
        private static readonly TimeSpan ServiceTypeCacheDuration = TimeSpan.FromMinutes(20);

        /// <summary>
        /// Do not cache services other than the ones we are currently seeing.
        /// </summary>
        private static readonly TimeSpan ServiceCacheDuration = TimeSpan.Zero;

        private readonly ISnapshotProvider<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>> topologyProvider;
        private readonly IExtensionLabelsParser extensionLabelsParser;
        private readonly IMonotonicTimer timer;
        private readonly FabricDiscoveryOptions options;
        private readonly ILogger<IslandGatewayTopologyMapperWorker> logger;
        private readonly RecurringTask recurringTask;

        private readonly Dictionary<DiscoveredServiceEx, IslandGatewayBackendService> serviceLookup = new(ReferenceEqualityComparer<DiscoveredServiceEx>.Default);
        private readonly Dictionary<DiscoveredServiceType, IslandGatewayParsedServiceType> serviceTypeLookup = new(ReferenceEqualityComparer<DiscoveredServiceType>.Default);

        private Snapshot<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>> currentSFTopology;

        private Snapshot<IReadOnlyList<IslandGatewayBackendService>> snapshot;
        private CancellationTokenSource changeToken;

        public IslandGatewayTopologyMapperWorker(
            ISnapshotProvider<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>> topologyProvider,
            IExtensionLabelsParser extensionLabelsParser,
            IMonotonicTimer timer,
            IProcessExiter processExiter,
            IOptions<FabricDiscoveryOptions> options,
            ILogger<IslandGatewayTopologyMapperWorker> logger,
            IOperationLogger operationLogger)
            : base(CreateWorkerOptions(options), processExiter, logger)
        {
            this.topologyProvider = topologyProvider ?? throw new ArgumentNullException(nameof(topologyProvider));
            this.extensionLabelsParser = extensionLabelsParser ?? throw new ArgumentNullException(nameof(extensionLabelsParser));
            this.timer = timer ?? throw new ArgumentNullException(nameof(timer));
            this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));

            this.recurringTask = new RecurringTask(this.RunIterationAsync)
                .WithLogging(logger, operationLogger, "IslandGatewayTopologyMapperWorker.Iteration")
                .WithAbortOnFailure() // This worker should never fail, a single failure is grounds to abort
                .WithIterationTimeout(options.Value.AbortAfterTimeoutInSeconds > 0 ? TimeSpan.FromSeconds(options.Value.AbortAfterTimeoutInSeconds) : null);
        }

        public Snapshot<IReadOnlyList<IslandGatewayBackendService>> GetSnapshot() => this.snapshot;

        protected override Task InitAsync(CancellationToken cancellation)
        {
            this.currentSFTopology = this.topologyProvider.GetSnapshot();
            if (this.currentSFTopology == null)
            {
                throw new InvalidOperationException($"Service Fabric topology should already be known when {nameof(IslandGatewayTopologyMapperWorker)} starts.");
            }

            this.UpdateFromSFTopology(this.currentSFTopology.Value, this.timer.CurrentTime);
            return Task.CompletedTask;
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
        /// The Service Manifest can specify a label with value <c>[AppParamName]</c>, in which case we replace it
        /// with the value of an application parameter with the given name <c>AppParamName</c>.
        /// Application parameter names are case insensitive in Service Fabric.
        /// If no such app param exists, we replace with empty string.
        /// </summary>
        private static Dictionary<string, string> ComputeEffectiveLabels(IReadOnlyDictionary<string, string> labels, DiscoveredAppEx app)
        {
            _ = labels ?? throw new ArgumentNullException(nameof(labels));
            _ = app ?? throw new ArgumentNullException(nameof(app));

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var effectiveAppParams = app.EffectiveAppParams;
            foreach (var label in labels)
            {
                string value = label.Value;
                if (value.Length > 2 && value[0] == '[' && value[value.Length - 1] == ']')
                {
                    string appParamName = value.Substring(1, value.Length - 2);
                    string appParamValue;
                    if (!effectiveAppParams.TryGetValue(appParamName, out appParamValue))
                    {
                        // Future: This should trigger a Warning or Error health report on the faulty service.
                        // This is not critical because if the absence of the setting leads to invalid configs, we *do* already report error
                        // (for example, if a route's rule were missing).
                        appParamValue = string.Empty;
                    }

                    value = appParamValue;
                }

                result.Add(label.Key, value);
            }

            return result;
        }

        private async Task RunIterationAsync(CancellationToken cancellation)
        {
            await this.currentSFTopology.ChangeToken.WaitForChanges(cancellation);

            // When we get here, it's because the SF topology has just changed.
            var newTopology = this.topologyProvider.GetSnapshot();
            var now = this.timer.CurrentTime;
            this.UpdateFromSFTopology(newTopology.Value, now);
            this.CleanCaches(now);

            // Only update the current topology at the end -- if anything fails before we get here, we want the next iteration to retry
            // (otherwise it might sit doing nothing until the next SF topology change.
            this.currentSFTopology = newTopology;
        }

        private void UpdateFromSFTopology(IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp> discoveredApps, TimeSpan now)
        {
            var newBackendServices = new List<IslandGatewayBackendService>();
            foreach (var app in discoveredApps.Values)
            {
                if (app is not DiscoveredAppEx appEx)
                {
                    // App was not selected for discovery by `TopologyDiscoveryFilter`, skip...
                    continue;
                }

                foreach (var service in appEx.Services.Values)
                {
                    if (service is not DiscoveredServiceEx serviceEx)
                    {
                        // Service was not selected for discovery by `TopologyDiscoveryFilter`, skip...
                        continue;
                    }

                    if (!this.serviceLookup.TryGetValue(serviceEx, out var igwBackendService))
                    {
                        igwBackendService = this.MapService(appEx, serviceEx, now);
                        if (igwBackendService == null)
                        {
                            continue;
                        }

                        this.serviceLookup[serviceEx] = igwBackendService;
                    }

                    igwBackendService.LastUsed = now;
                    newBackendServices.Add(igwBackendService);
                }
            }

            newBackendServices.Sort((a, b) => string.CompareOrdinal(a.FabricService.Service.ServiceName.Value.ToString(), b.FabricService.Service.ServiceName.Value.ToString()));
            if (this.snapshot == null ||
                !ListEqualityUtil.AreListsEqual(this.snapshot.Value, newBackendServices, ReferenceEqualityComparer<IslandGatewayBackendService>.Default))
            {
                this.UpdateSnapshot(newBackendServices);
            }
        }

        private IslandGatewayBackendService MapService(DiscoveredAppEx appEx, DiscoveredServiceEx serviceEx, TimeSpan now)
        {
            if (!this.serviceTypeLookup.TryGetValue(serviceEx.ServiceType, out var igwServiceType))
            {
                igwServiceType = this.serviceTypeLookup[serviceEx.ServiceType] = this.MapServiceType(serviceEx.ServiceType);
            }

            igwServiceType.LastUsed = now;
            if (igwServiceType.RawLabels == null)
            {
                return null;
            }

            var effectiveLabels = ComputeEffectiveLabels(igwServiceType.RawLabels, appEx);
            return new IslandGatewayBackendService
            {
                FabricApplication = appEx,
                FabricService = serviceEx,
                ParsedServiceType = igwServiceType,
                EffectiveLabels = effectiveLabels,
                LabelOverrides = null, // If there are overrides from Properties, the next stage in the pipeline (`IslandGatewayConfigProducerWorker`) will add them
                FinalEffectiveLabels = effectiveLabels,
            };
        }

        private IslandGatewayParsedServiceType MapServiceType(DiscoveredServiceType serviceType)
        {
            Dictionary<string, string> labels = null;

            var extensions = serviceType.ServiceType.Extensions;
            if (extensions != null &&
                extensions.TryGetValue(Consts.ServiceManifestExtensionName, out var extensionXml) &&
                extensionXml != null)
            {
                this.extensionLabelsParser.TryExtractLabels(extensionXml, out labels);
            }

            return new IslandGatewayParsedServiceType
            {
                FabricServiceType = serviceType,
                RawLabels = labels,
            };
        }

        private void CleanCaches(TimeSpan now)
        {
            var servicesToClean = this.serviceLookup
                .Where(kvp => kvp.Value.LastUsed < now - ServiceCacheDuration)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var serviceToClean in servicesToClean)
            {
                this.serviceLookup.Remove(serviceToClean);
            }

            var serviceTypesToClean = this.serviceTypeLookup
                .Where(kvp => kvp.Value.LastUsed < now - ServiceTypeCacheDuration)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var serviceTypeToClean in serviceTypesToClean)
            {
                this.serviceTypeLookup.Remove(serviceTypeToClean);
            }
        }

        private void UpdateSnapshot(List<IslandGatewayBackendService> backendServices)
        {
            this.logger.LogInformation($"Signaling new Island Gateway topology. {backendServices.Count} backend services.");

            using var oldToken = this.changeToken;
            this.changeToken = new CancellationTokenSource();
            this.snapshot = new Snapshot<IReadOnlyList<IslandGatewayBackendService>>(backendServices, new CancellationChangeToken(this.changeToken.Token));

            try
            {
                oldToken?.Cancel(throwOnFirstException: false);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error signaling change.");
            }
        }
    }
}
