// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.FabricDiscovery.FabricWrapper;
using Yarp.ServiceFabric.FabricDiscovery.Util;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
{
    /// <summary>
    /// Manages discovery of services that use Service Manifest extensions as their discovery model.
    /// </summary>
    internal class ServiceDiscoveryManager : IServiceDiscoveryManager
    {
        private readonly DirtyServicesTracker dirtyServices = new DirtyServicesTracker();
        private readonly Random random = new Random();

        private readonly IAppTypeDiscoveryManager appTypeDiscoveryManager;
        private readonly IQueryClientWrapper queryClient;
        private readonly IServiceManagementClientWrapper serviceManagementClient;
        private readonly IServiceDiscoveryHelper discoveryHelper;
        private readonly FabricDiscoveryMetrics metrics;
        private readonly ILogger<ServiceDiscoveryManager> logger;
        private readonly IOperationLogger operationLogger;

        /// <summary>
        /// The contents of <see cref="discoveredApps"/> are an immutable snapshot of the topology at any point in time.
        /// This makes threading concerns trivial, as there is no data mutation involved. Whenever topology changes,
        /// we replace the contents entirely, making this point to a new root that represents another immutable snapshot.
        /// </summary>
        private IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp> discoveredApps = new Dictionary<ApplicationNameKey, DiscoveredApp>();
        private Dictionary<ServiceNameKey, DiscoveredAppEx> serviceToAppLookup = new ();

        public ServiceDiscoveryManager(
            IAppTypeDiscoveryManager appTypeDiscoveryManager,
            IQueryClientWrapper queryClient,
            IServiceManagementClientWrapper serviceManagementClient,
            IServiceDiscoveryHelper discoveryHelper,
            FabricDiscoveryMetrics metrics,
            ILogger<ServiceDiscoveryManager> logger,
            IOperationLogger operationLogger)
        {
            this.appTypeDiscoveryManager = appTypeDiscoveryManager ?? throw new ArgumentNullException(nameof(appTypeDiscoveryManager));
            this.queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
            this.serviceManagementClient = serviceManagementClient ?? throw new ArgumentNullException(nameof(serviceManagementClient));
            this.discoveryHelper = discoveryHelper ?? throw new ArgumentNullException(nameof(discoveryHelper));
            this.metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
        }

        private enum GetParentAppOutcome
        {
            Success,
            NotFound,
            RediscoveredApp,
        }

        public IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp> DiscoveredApps => this.discoveredApps;

        public async Task<Func<CancellationToken, Task>> SubscribeToFabricEvents(CancellationToken cancellation)
        {
            return await this.serviceManagementClient.SubscribeToNotifications(OnNotification, Consts.DefaultFabricApiTimeout, cancellation);

            void OnNotification(ServiceNotificationWrapper notification)
            {
                this.logger.LogInformation(
                    "SF notification: " + JsonSerializer.Serialize(
                        new
                        {
                            notification.ServiceName,
                            notification.PartitionId,
                            notification.Endpoints,
                            notification.PartitionInfo,
                        }));
                this.metrics.FabricDiscoveryNotifications(1);
                this.dirtyServices.Mark(notification.ServiceName);
            }
        }

        public Task RefreshAll(CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteAsync(
                "ServiceDiscoveryManager.RefreshAll",
                async () =>
                {
                    var newDiscoveredApps = new Dictionary<ApplicationNameKey, DiscoveredApp>();
                    var newServiceToAppLookup = new Dictionary<ServiceNameKey, DiscoveredAppEx>();

                    // Clear `dirtyServices` **before** starting to refresh so that we won't miss items that may be added while we are refreshing.
                    // Note that if the refresh fails we must reinstate these dirty items.
                    var rollbacker = this.dirtyServices.UnmarkAll();
                    try
                    {
                        foreach (var appTypeName in this.appTypeDiscoveryManager.GetInterestingAppTypeNames())
                        {
                            await foreach (var application in this.queryClient.GetApplicationsAsync(new ApplicationQueryDescription { ApplicationTypeNameFilter = appTypeName }, Consts.DefaultFabricApiTimeout, cancellation))
                            {
                                // Note: this may produce null if the app does not exist when we get here.
                                var discoveredApp = await this.discoveryHelper.DiscoverApp(application, cancellation);
                                if (discoveredApp == null)
                                {
                                    continue;
                                }

                                newDiscoveredApps.Add(application.ApplicationName, discoveredApp);
                                if (discoveredApp is DiscoveredAppEx discoveredAppEx)
                                {
                                    foreach (var serviceName in discoveredAppEx.Services.Keys)
                                    {
                                        newServiceToAppLookup.Add(serviceName, discoveredAppEx);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Refresh failed, so everything that was dirty before remains dirty now...
                        rollbacker.Rollback();
                        throw;
                    }

                    this.discoveredApps = newDiscoveredApps;
                    this.serviceToAppLookup = newServiceToAppLookup;
                });
        }

        public Task<int> RefreshDirtyServices(CancellationToken cancellation)
        {
            var dirtyServices = this.dirtyServices.GetSnapshot();
            KeyValuePair<string, string>[] properties = new[]
                {
                    KeyValuePair.Create("numDirty", dirtyServices.Count.ToString()),
                };
            return this.operationLogger.ExecuteAsync(
                "ServiceDiscoveryManager.RefreshDirtyServices",
                async () =>
                {
                    foreach (var serviceName in dirtyServices)
                    {
                        await this.RefreshService(serviceName, cancellation);
                    }

                    return dirtyServices.Count;
                },
                properties);
        }

        private Task RefreshService(ServiceNameKey serviceName, CancellationToken cancellation)
        {
            KeyValuePair<string, string>[] properties = new[]
                {
                    KeyValuePair.Create(nameof(serviceName), serviceName.ToString()),
                };
            return this.operationLogger.ExecuteAsync(
                "ServiceDiscoveryManager.RefreshService",
                async () =>
                {
                    // Drop from `dirtyServices` **before** starting to refresh so that we won't miss this same service if it is re-added while we are refreshing.
                    // Note that if the refresh fails we must reinstate it.
                    var rollbacker = this.dirtyServices.Unmark(new List<ServiceNameKey>(1) { serviceName });
                    try
                    {
                        await this.RefreshServiceCore(serviceName, cancellation);
                    }
                    catch
                    {
                        // Refresh failed, so everything that was dirty before remains dirty now...
                        rollbacker.Rollback();
                        throw;
                    }
                },
                properties);
        }

        private async Task RefreshServiceCore(ServiceNameKey serviceName, CancellationToken cancellation)
        {
            var (outcome, discoveredAppEx) = await this.GetParentApp(serviceName, cancellation);
            if (outcome != GetParentAppOutcome.Success)
            {
                return;
            }

            if (discoveredAppEx == null)
            {
                // Successfully determined that we are not interested in the app that the service belongs to.
                return;
            }

            ServiceWrapper service = null;
            try
            {
                service = await
                    this.queryClient.GetServicesAsync(new ServiceQueryDescription(discoveredAppEx.Application.ApplicationName) { ServiceNameFilter = serviceName }, Consts.DefaultFabricApiTimeout, cancellation)
                    .FirstOrDefaultAsync();
            }
            catch (FabricElementNotFoundException)
            {
                // App does not exist... Fall through as the service also doesn't exist in this case.
            }

            if (service == null)
            {
                this.logger.LogInformation($"Attempted to refresh a service that doesn't exist. ServiceName='{serviceName}', AppName='{discoveredAppEx.Application.ApplicationName}'");

                this.UpdateOrRemoveService(discoveredAppEx, serviceName, null);
                return;
            }

            if (!discoveredAppEx.AppType.ServiceTypes.TryGetValue(service.ServiceTypeName, out var serviceType) ||
                serviceType.ServiceType.ServiceManifestVersion != service.ServiceManifestVersion)
            {
                // Service metadata does not match what we knew about the app. We need to refresh the app (which will also refresh the associated AppType)
                this.logger.LogInformation(
                    $"Detected app upgrade via ServiceManifestVersion mismatch while refreshing service (ServiceName='{serviceName}', AppName='{discoveredAppEx.Application.ApplicationName}', ServiceManifestVersion='{service.ServiceManifestVersion}'). " +
                    $"Previous AppTypeName='{discoveredAppEx.AppType.AppType.ApplicationTypeName}', AppTypeVersion='{discoveredAppEx.AppType.AppType.ApplicationTypeVersion}', ServiceManifestVersion='{serviceType?.ServiceType.ServiceManifestVersion ?? "<unknown>"}'.");
                await this.RefreshAppSubtree(discoveredAppEx.Application.ApplicationName, cancellation);
                return;
            }

            {
                var newDiscoveredService = await this.discoveryHelper.DiscoverService(discoveredAppEx, serviceType, service, cancellation);
                this.UpdateOrRemoveService(discoveredAppEx, serviceName, newDiscoveredService);
            }
        }

        private async Task<(GetParentAppOutcome Outcome, DiscoveredAppEx AppOrNull)> GetParentApp(ServiceNameKey serviceName, CancellationToken cancellation)
        {
            if (this.serviceToAppLookup.TryGetValue(serviceName, out var discoveredAppEx))
            {
                // We already knew this service's app, this is safe to re-use -- services cannot move between applications.
                // Although the discovered `discoveredAppEx` **could** be out of date (i.e. may be for a different AppType Version than the service is currently running).
                // It is the caller's responsibility to verify and remediate this.
                return (GetParentAppOutcome.Success, discoveredAppEx);
            }

            ApplicationNameKey applicationName;
            try
            {
                applicationName = await this.queryClient.GetApplicationNameAsync(serviceName, Consts.DefaultFabricApiTimeout, cancellation);
            }
            catch (FabricServiceNotFoundException)
            {
                this.logger.LogInformation($"Attempted to query a non-existent service. ServiceName:'{serviceName}'");
                return (GetParentAppOutcome.NotFound, null);
            }

            if (!this.discoveredApps.TryGetValue(applicationName, out var discoveredApp))
            {
                // We haven't seen this app yet, take the opportunity to discover the entire app.
                await this.RefreshAppSubtree(applicationName, cancellation);
                return (GetParentAppOutcome.RediscoveredApp, null);
            }

            // Note: if `discoveredApp is not DiscoveredAppEx` it means we are not interested in that app (via TopologyDiscoveryFilter)
            return (GetParentAppOutcome.Success, discoveredApp as DiscoveredAppEx);
        }

        private Task RefreshAppSubtree(ApplicationNameKey applicationName, CancellationToken cancellation)
        {
            KeyValuePair<string, string>[] properties = new[]
                {
                    KeyValuePair.Create(nameof(applicationName), applicationName.ToString()),
                };
            return this.operationLogger.ExecuteAsync(
                "ServiceDiscoveryManager.RefreshAppSubtree",
                async () =>
                {
                    DirtyServicesTracker.IRollbacker rollbacker = null;
                    if (this.discoveredApps.TryGetValue(applicationName, out var oldDiscoveredApp) &&
                        oldDiscoveredApp is DiscoveredAppEx oldDiscoveredAppEx)
                    {
                        rollbacker = this.dirtyServices.Unmark(oldDiscoveredAppEx.Services.Keys.ToList());
                    }

                    try
                    {
                        var app = await this.queryClient
                            .GetApplicationsAsync(new ApplicationQueryDescription { ApplicationNameFilter = applicationName }, Consts.DefaultFabricApiTimeout, cancellation)
                            .FirstOrDefaultAsync();

                        DiscoveredApp newDiscoveredApp = null;
                        if (app != null)
                        {
                            // Note: this may produce null if the app does not exist when we get here.
                            newDiscoveredApp = await this.discoveryHelper.DiscoverApp(app, cancellation);
                        }

                        if (newDiscoveredApp == null)
                        {
                            this.logger.LogInformation($"Attempted to refresh an app that does not exist. AppName='{applicationName}'");
                        }

                        this.UpdateOrRemoveApp(applicationName, newDiscoveredApp);
                    }
                    catch
                    {
                        // Refresh failed, so everything that was dirty before remains dirty now...
                        rollbacker?.Rollback();
                        throw;
                    }
                },
                properties);
        }

        /// <summary>
        /// Creates and sets a new immutable snapshot (<see cref="discoveredApps"/>) by creating a new <see cref="DiscoveredAppEx"/>
        /// entry to represent the mutated service, and propagating the change up the chain.
        /// The reverse service-to-app lookup is also updated accordingly.
        /// </summary>
        /// <remarks>
        /// Sub-trees of elements that are changing are preserved, which is safe since they are immutable.
        /// </remarks>
        private void UpdateOrRemoveService(DiscoveredAppEx discoveredAppEx, ServiceNameKey serviceName, DiscoveredService newDiscoveredService)
        {
            var newServices = discoveredAppEx.Services.ShallowClone();
            if (newDiscoveredService != null)
            {
                newServices[serviceName] = newDiscoveredService;
            }
            else
            {
                newServices.Remove(serviceName);
            }

            var newDiscoveredAppEx = new DiscoveredAppEx(new DiscoveredApp(discoveredAppEx.Application), discoveredAppEx.AppType, newServices);
            this.UpdateOrRemoveApp(discoveredAppEx.Application.ApplicationName, newDiscoveredAppEx);
        }

        /// <summary>
        /// Creates and sets a new immutable snapshot (<see cref="discoveredApps"/>) by replacing an existing app entry, if any,
        /// with a new entry for <paramref name="newDiscoveredAppOrNull"/>, if any.
        /// The reverse service-to-app lookup is also updated accordingly.
        /// </summary>
        private void UpdateOrRemoveApp(ApplicationNameKey applicationName, DiscoveredApp newDiscoveredAppOrNull)
        {
            // Clear old reverse lookup entries (e.g. some services may no longer exist)
            if (this.discoveredApps.TryGetValue(applicationName, out var oldDiscoveredApp) &&
                oldDiscoveredApp is DiscoveredAppEx oldDiscoveredAppEx)
            {
                foreach (var serviceName in oldDiscoveredAppEx.Services.Keys)
                {
                    this.serviceToAppLookup.Remove(serviceName);
                }
            }

            if (newDiscoveredAppOrNull != null)
            {
                // Add new reverse lookup entries
                if (newDiscoveredAppOrNull is DiscoveredAppEx newDiscoveredAppEx)
                {
                    foreach (var serviceName in newDiscoveredAppEx.Services.Keys)
                    {
                        this.serviceToAppLookup[serviceName] = newDiscoveredAppEx;
                    }
                }

                var newDiscoveredApps = this.discoveredApps.ShallowClone();
                newDiscoveredApps[applicationName] = newDiscoveredAppOrNull;
                this.discoveredApps = newDiscoveredApps;
            }
            else if (oldDiscoveredApp != null)
            {
                var newDiscoveredApps = this.discoveredApps.ShallowClone();
                newDiscoveredApps.Remove(applicationName);
                this.discoveredApps = newDiscoveredApps;
            }
        }
    }
}
