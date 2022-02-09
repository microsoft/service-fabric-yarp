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

namespace Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig
{
    /// <summary>
    /// Hosted service responsible for producing SFYarp abstractions from the Service Fabric topology,
    /// as an intermediary step before additional processing steps performed specifically for SFYarp.
    /// The processing sequence is
    ///   * <see cref="TopologyDiscoveryWorker"/>, then
    ///   * this, then
    ///   * <see cref="SFYarpConfigProducerWorker"/>
    ///   * <see cref="SFYarpConfigSerializerWorker"/>.
    /// </summary>
    internal class SFYarpTopologyMapperWorker : CriticalBackgroundService, ISnapshotProvider<IReadOnlyList<SFYarpBackendService>>
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
        private readonly ILogger<SFYarpTopologyMapperWorker> logger;
        private readonly RecurringTask recurringTask;

        private readonly Dictionary<DiscoveredServiceEx, SFYarpBackendService> serviceLookup = new(ReferenceEqualityComparer<DiscoveredServiceEx>.Default);
        private readonly Dictionary<DiscoveredServiceType, SFYarpParsedServiceType> serviceTypeLookup = new(ReferenceEqualityComparer<DiscoveredServiceType>.Default);

        private Snapshot<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>> currentSFTopology;

        private Snapshot<IReadOnlyList<SFYarpBackendService>> snapshot;
        private CancellationTokenSource changeToken;

        public SFYarpTopologyMapperWorker(
            ISnapshotProvider<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>> topologyProvider,
            IExtensionLabelsParser extensionLabelsParser,
            IMonotonicTimer timer,
            IProcessExiter processExiter,
            IOptions<FabricDiscoveryOptions> options,
            ILogger<SFYarpTopologyMapperWorker> logger,
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
                .WithLogging(logger, operationLogger, "SFYarpTopologyMapperWorker.Iteration")
                .WithAbortOnFailure() // This worker should never fail, a single failure is grounds to abort
                .WithIterationTimeout(options.Value.AbortAfterTimeoutInSeconds > 0 ? TimeSpan.FromSeconds(options.Value.AbortAfterTimeoutInSeconds) : null);
        }

        public Snapshot<IReadOnlyList<SFYarpBackendService>> GetSnapshot() => this.snapshot;

        protected override Task InitAsync(CancellationToken cancellation)
        {
            this.currentSFTopology = this.topologyProvider.GetSnapshot();
            if (this.currentSFTopology == null)
            {
                throw new InvalidOperationException($"Service Fabric topology should already be known when {nameof(SFYarpTopologyMapperWorker)} starts.");
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
            var newBackendServices = new List<SFYarpBackendService>();
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

                    if (!this.serviceLookup.TryGetValue(serviceEx, out var sfyBackendService))
                    {
                        sfyBackendService = this.MapService(appEx, serviceEx, now);
                        if (sfyBackendService == null)
                        {
                            continue;
                        }

                        this.serviceLookup[serviceEx] = sfyBackendService;
                    }

                    sfyBackendService.LastUsed = now;
                    newBackendServices.Add(sfyBackendService);
                }
            }

            newBackendServices.Sort((a, b) => string.CompareOrdinal(a.FabricService.Service.ServiceName.Value.ToString(), b.FabricService.Service.ServiceName.Value.ToString()));
            if (this.snapshot == null ||
                !ListEqualityUtil.AreListsEqual(this.snapshot.Value, newBackendServices, ReferenceEqualityComparer<SFYarpBackendService>.Default))
            {
                this.UpdateSnapshot(newBackendServices);
            }
        }

        private SFYarpBackendService MapService(DiscoveredAppEx appEx, DiscoveredServiceEx serviceEx, TimeSpan now)
        {
            if (!this.serviceTypeLookup.TryGetValue(serviceEx.ServiceType, out var sfyServiceType))
            {
                sfyServiceType = this.serviceTypeLookup[serviceEx.ServiceType] = this.MapServiceType(serviceEx.ServiceType);
            }

            sfyServiceType.LastUsed = now;
            if (sfyServiceType.RawLabels == null)
            {
                return null;
            }

            var effectiveLabels = ComputeEffectiveLabels(sfyServiceType.RawLabels, appEx);
            return new SFYarpBackendService
            {
                FabricApplication = appEx,
                FabricService = serviceEx,
                ParsedServiceType = sfyServiceType,
                EffectiveLabels = effectiveLabels,
                LabelOverrides = null, // If there are overrides from Properties, the next stage in the pipeline (`SFYarpConfigProducerWorker`) will add them
                FinalEffectiveLabels = effectiveLabels,
            };
        }

        private SFYarpParsedServiceType MapServiceType(DiscoveredServiceType serviceType)
        {
            Dictionary<string, string> labels = null;

            var extensions = serviceType.ServiceType.Extensions;
            if (extensions != null &&
                extensions.TryGetValue(Consts.ServiceManifestExtensionName, out var extensionXml) &&
                extensionXml != null)
            {
                this.extensionLabelsParser.TryExtractLabels(extensionXml, out labels);
            }

            return new SFYarpParsedServiceType
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

        private void UpdateSnapshot(List<SFYarpBackendService> backendServices)
        {
            this.logger.LogInformation($"Signaling new SFYarp topology. {backendServices.Count} backend services.");

            using var oldToken = this.changeToken;
            this.changeToken = new CancellationTokenSource();
            this.snapshot = new Snapshot<IReadOnlyList<SFYarpBackendService>>(backendServices, new CancellationChangeToken(this.changeToken.Token));

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
