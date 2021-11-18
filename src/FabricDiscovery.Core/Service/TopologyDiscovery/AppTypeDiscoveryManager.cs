// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.FabricDiscovery.FabricWrapper;
using Yarp.ServiceFabric.FabricDiscovery.Util;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
{
    internal class AppTypeDiscoveryManager : IAppTypeDiscoveryManager
    {
        private readonly IQueryClientWrapper queryClient;
        private readonly TopologyDiscoveryFilter filter;
        private readonly ILogger<AppTypeDiscoveryManager> logger;
        private readonly IOperationLogger operationLogger;
        private Dictionary<ApplicationTypeNameKey, Dictionary<ApplicationTypeVersionKey, DiscoveredAppType>> appTypes = new();

        public AppTypeDiscoveryManager(
            IQueryClientWrapper queryClient,
            TopologyDiscoveryFilter filter,
            ILogger<AppTypeDiscoveryManager> logger,
            IOperationLogger operationLogger)
        {
            this.queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
            this.filter = filter ?? throw new ArgumentNullException(nameof(filter));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
        }

        /// <summary>
        /// Return AppType names for which we fully discovered the subtree of at least one AppType version.
        /// This helps us skip entire AppTypes that were deemed uninsteresting by
        /// <see cref="TopologyDiscoveryFilter.ShouldDiscoverAppType"/>.
        /// </summary>
        public virtual IList<ApplicationTypeNameKey> GetInterestingAppTypeNames()
        {
            return this.appTypes
                .Where(kvp => kvp.Value.Values.Any(appType => appType is DiscoveredAppTypeEx))
                .Select(kvp => kvp.Key)
                .ToList();
        }

        public virtual Task Refresh(CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteAsync(
                "AppTypeDiscoveryManager.Refresh",
                async () =>
                {
                    var newAppTypes = new Dictionary<ApplicationTypeNameKey, Dictionary<ApplicationTypeVersionKey, DiscoveredAppType>>();

                    await foreach (var appType in this.queryClient.GetApplicationTypesAsync(new PagedApplicationTypeQueryDescription(), Consts.DefaultFabricApiTimeout, cancellation))
                    {
                        if (!newAppTypes.TryGetValue(appType.ApplicationTypeName, out var appTypeVersions))
                        {
                            appTypeVersions = new Dictionary<ApplicationTypeVersionKey, DiscoveredAppType>();
                            newAppTypes.Add(appType.ApplicationTypeName, appTypeVersions);
                        }

                        // Note: this may produce null if the AppType no longer exists when we get here.
                        var discoveredAppType = await this.DiscoverAppType(appType, cancellation);
                        if (discoveredAppType != null)
                        {
                            appTypeVersions.Add(discoveredAppType.AppType.ApplicationTypeVersion, discoveredAppType);
                        }
                    }

                    this.appTypes = newAppTypes;
                });
        }

        public virtual async Task<DiscoveredAppType> GetAppTypeInfo(ApplicationTypeNameKey appTypeName, ApplicationTypeVersionKey appTypeVersion, CancellationToken cancellation)
        {
            // NOTE: App Type Versions are immutable but not quite --
            // an App Type Version could be unprovisioned and a different one with the same name/version could be deployed in its place.
            // In the unlikely event that this happens, we will sort it out next time `Refresh` is called and the entire tree is rebuilt.
            if (!this.appTypes.TryGetValue(appTypeName, out var appTypeVersions) ||
                !appTypeVersions.TryGetValue(appTypeVersion, out var discoveredAppType))
            {
                // We haven't seen this App Type name/version yet, time to discover it
                var appType = await this.queryClient
                    .GetApplicationTypesAsync(
                        new PagedApplicationTypeQueryDescription
                        {
                            ApplicationTypeNameFilter = appTypeName,
                            ApplicationTypeVersionFilter = appTypeVersion,
                        },
                        Consts.DefaultFabricApiTimeout)
                    .FirstOrDefaultAsync(cancellation);

                discoveredAppType = null;
                if (appType != null)
                {
                    // Note: this may produce null if the AppType no longer exists when we get here.
                    discoveredAppType = await this.DiscoverAppType(appType, cancellation);
                }

                if (discoveredAppType == null)
                {
                    // This could happen e.g. if an App Type is unprovisioned soon after we had enumerated an app (in which case the app would be deleted by now). This should be very rare.
                    this.logger.LogWarning($"AppType not found, AppTypeName={appTypeName}, AppTypeVersion={appTypeVersion}. Was it just unprovisioned?");
                    return null;
                }

                if (appTypeVersions == null)
                {
                    appTypeVersions = new Dictionary<ApplicationTypeVersionKey, DiscoveredAppType>();
                    this.appTypes.Add(appTypeName, appTypeVersions);
                }

                appTypeVersions.Add(appTypeVersion, discoveredAppType);
            }

            return discoveredAppType;
        }

        private Task<DiscoveredAppType> DiscoverAppType(ApplicationTypeWrapper appType, CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteAsync(
                "AppTypeDiscoveryManager.DiscoverAppType",
                async () =>
                {
                    DiscoveredAppType discoveredAppType = new DiscoveredAppType(appType);
                    if (this.filter.ShouldDiscoverAppType(appType))
                    {
                        // Note: This may produce null if the AppType no longer exists when we get here.
                        discoveredAppType = await this.DiscoverAppTypeSubtree(discoveredAppType, cancellation);
                    }

                    return discoveredAppType;
                },
                new[]
                {
                    KeyValuePair.Create(nameof(appType.ApplicationTypeName), appType.ApplicationTypeName.ToString()),
                    KeyValuePair.Create(nameof(appType.ApplicationTypeVersion), appType.ApplicationTypeVersion.ToString()),
                });
        }

        private async Task<DiscoveredAppTypeEx> DiscoverAppTypeSubtree(DiscoveredAppType appType, CancellationToken cancellation)
        {
            var serviceTypes = new Dictionary<ServiceTypeNameKey, DiscoveredServiceType>();

            try
            {
                await foreach (var serviceType in this.queryClient.GetServiceTypesAsync(appType.AppType.ApplicationTypeName, appType.AppType.ApplicationTypeVersion, Consts.DefaultFabricApiTimeout, cancellation))
                {
                    serviceTypes.Add(serviceType.ServiceTypeName, new DiscoveredServiceType(serviceType));
                }
            }
            catch (FabricElementNotFoundException)
            {
                // AppType does not exist.
                return null;
            }

            return new DiscoveredAppTypeEx(appType, serviceTypes);
        }
    }
}
