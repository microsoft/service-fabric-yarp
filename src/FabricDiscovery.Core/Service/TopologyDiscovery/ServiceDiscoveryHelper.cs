// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.FabricDiscovery.FabricWrapper;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
{
    internal class ServiceDiscoveryHelper : IServiceDiscoveryHelper
    {
        private readonly IQueryClientWrapper queryClient;
        private readonly IAppTypeDiscoveryManager appTypeDiscoveryManager;
        private readonly TopologyDiscoveryFilter filter;
        private readonly ILogger<ServiceDiscoveryHelper> logger;
        private readonly IOperationLogger operationLogger;

        public ServiceDiscoveryHelper(
            IQueryClientWrapper queryClient,
            IAppTypeDiscoveryManager appTypeDiscoveryManager,
            TopologyDiscoveryFilter filter,
            ILogger<ServiceDiscoveryHelper> logger,
            IOperationLogger operationLogger)
        {
            this.queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
            this.appTypeDiscoveryManager = appTypeDiscoveryManager ?? throw new ArgumentNullException(nameof(appTypeDiscoveryManager));
            this.filter = filter ?? throw new ArgumentNullException(nameof(filter));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
        }

        public Task<DiscoveredApp> DiscoverApp(ApplicationWrapper application, CancellationToken cancellation)
        {
            _ = application ?? throw new ArgumentNullException(nameof(application));

            KeyValuePair<string, string>[] properties = new[]
                {
                    KeyValuePair.Create(nameof(application.ApplicationName), application.ApplicationName.ToString()),
                    KeyValuePair.Create(nameof(application.ApplicationTypeName), application.ApplicationTypeName.ToString()),
                    KeyValuePair.Create(nameof(application.ApplicationTypeVersion), application.ApplicationTypeVersion.ToString()),
                };
            return this.operationLogger.ExecuteAsync(
                "ServiceDiscoveryHelper.DiscoverApp",
                async () =>
                {
                    var discoveredApp = new DiscoveredApp(application);

                    var appType = await this.appTypeDiscoveryManager.GetAppTypeInfo(application.ApplicationTypeName, application.ApplicationTypeVersion, cancellation);
                    if (appType is DiscoveredAppTypeEx appTypeEx)
                    {
                        if (this.filter.ShouldDiscoverApp(appTypeEx, discoveredApp))
                        {
                            // Note: this may produce null if the app does not exist when we get here.
                            discoveredApp = await this.DiscoverAppSubtree(appTypeEx, discoveredApp, cancellation);
                        }
                    }

                    return discoveredApp;
                },
                properties);
        }

        public Task<DiscoveredService> DiscoverService(DiscoveredApp app, DiscoveredServiceType serviceType, ServiceWrapper service, CancellationToken cancellation)
        {
            _ = app ?? throw new ArgumentNullException(nameof(app));
            _ = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
            _ = service ?? throw new ArgumentNullException(nameof(service));

            KeyValuePair<string, string>[] properties = new[]
                {
                    KeyValuePair.Create(nameof(app.Application.ApplicationName), app.Application.ApplicationName.ToString()),
                    KeyValuePair.Create(nameof(app.Application.ApplicationTypeName), app.Application.ApplicationTypeName.ToString()),
                    KeyValuePair.Create(nameof(app.Application.ApplicationTypeVersion), app.Application.ApplicationTypeVersion.ToString()),
                    KeyValuePair.Create(nameof(serviceType.ServiceType.ServiceTypeName), serviceType.ServiceType.ServiceTypeName.ToString()),
                    KeyValuePair.Create(nameof(serviceType.ServiceType.ServiceManifestVersion), serviceType.ServiceType.ServiceManifestVersion.ToString()),
                };
            return this.operationLogger.ExecuteAsync(
                "ServiceDiscoveryHelper.DiscoverService",
                async () =>
                {
                    var discoveredService = new DiscoveredService(serviceType, service);
                    if (this.filter.ShouldDiscoverService(app, discoveredService))
                    {
                        try
                        {
                            discoveredService = await this.DiscoverServiceSubtree(discoveredService, cancellation);
                        }
                        catch (FabricServiceNotFoundException ex)
                        {
                            this.logger.LogWarning(ex, $"Unable to discover service, it doesn't exist. AppName='{app.Application.ApplicationName}', ServiceName='{service.ServiceName}', ServiceStatus='{service.ServiceStatus}'.");
                        }
                    }

                    return discoveredService;
                },
                properties);
        }

        private Task<DiscoveredServiceEx> DiscoverServiceSubtree(DiscoveredService service, CancellationToken cancellation)
        {
            _ = service ?? throw new ArgumentNullException(nameof(service));

            KeyValuePair<string, string>[] properties = new[]
                {
                    KeyValuePair.Create(nameof(service.Service.ServiceName), service.Service.ServiceName.ToString()),
                    KeyValuePair.Create(nameof(service.Service.ServiceTypeName), service.Service.ServiceTypeName.ToString()),
                    KeyValuePair.Create(nameof(service.Service.ServiceManifestVersion), service.Service.ServiceManifestVersion.ToString()),
                };
            return this.operationLogger.ExecuteAsync(
                "ServiceDiscoveryHelper.DiscoverServiceSubtree",
                async () =>
                {
                    List<DiscoveredPartition> partitions;
                    try
                    {
                        partitions = await this.DiscoverPartitionsAsync(service, cancellation);
                    }
                    catch (FabricException ex) when (ex is FabricServiceNotFoundException || ex.ErrorCode == FabricErrorCode.PartitionNotFound)
                    {
                        // Go ahead as if there were no partitions. This is the best we can do in this case...
                        this.logger.LogWarning(ex, $"Unable to discover service partitions, perhaps the service is being deleted. ServiceName='{service.Service.ServiceName}', ServiceStatus='{service.Service.ServiceStatus}'.");
                        partitions = new List<DiscoveredPartition>();
                    }

                    return new DiscoveredServiceEx(service, partitions);
                },
                properties);
        }

        private async Task<DiscoveredAppEx> DiscoverAppSubtree(DiscoveredAppTypeEx appType, DiscoveredApp app, CancellationToken cancellation)
        {
            _ = app ?? throw new ArgumentNullException(nameof(app));

            // Note: this may produce null if the app does not exist when we get here.
            var services = await this.DiscoverServices(appType, app, cancellation);
            return services == null ? null : new DiscoveredAppEx(app, appType, services);
        }

        private async Task<Dictionary<ServiceNameKey, DiscoveredService>> DiscoverServices(DiscoveredAppTypeEx appType, DiscoveredApp app, CancellationToken cancellation)
        {
            var services = new Dictionary<ServiceNameKey, DiscoveredService>();
            foreach (var (serviceTypeName, serviceType) in appType.ServiceTypes)
            {
                if (!this.filter.ShouldDiscoverServicesOfServiceType(app, serviceType))
                {
                    continue;
                }

                try
                {
                    await foreach (var service in
                        this.queryClient.GetServicesAsync(new ServiceQueryDescription(app.Application.ApplicationName) { ServiceTypeNameFilter = serviceTypeName }, Consts.DefaultFabricApiTimeout, cancellation))
                    {
                        try
                        {
                            DiscoveredService discoveredService = await this.DiscoverService(app, serviceType, service, cancellation);
                            services.Add(service.ServiceName, discoveredService);
                        }
                        catch (FabricElementNotFoundException ex)
                        {
                            throw new InvalidOperationException($"Unexpected {nameof(FabricElementNotFoundException)} while discovering a service. This is a coding defect.", ex);
                        }
                    }
                }
                catch (FabricElementNotFoundException)
                {
                    // This means the entire **application** no longer exists, and `queryClient.GetServicesAsync` is what failed, not the code within the foreach loop.
                    // Note 1: operations that fail because a **service** does not exist anymore
                    // produce a `FabricServiceNotFoundException` instead, and are handled within `this.DiscoverService`.
                    // Note 2: this is safe to swallow because `queryClient.GetServicesAsync` would have already logged the exception.
                    return null;
                }
            }

            return services;
        }

        private async Task<List<DiscoveredPartition>> DiscoverPartitionsAsync(DiscoveredService service, CancellationToken cancellation)
        {
            var result = new List<DiscoveredPartition>();
            await foreach (var partition in this.queryClient.GetPartitionsAsync(service.Service.ServiceName, Consts.DefaultFabricApiTimeout, cancellation))
            {
                var replicas = await this.DiscoverReplicasAsync(partition, cancellation);

                // Future: Store more metadata from partition like partition name for named partitions or range for range partitions.
                var discoveredPartition = new DiscoveredPartition(partition, replicas);
                result.Add(discoveredPartition);
            }

            return result;
        }

        private async Task<List<DiscoveredReplica>> DiscoverReplicasAsync(PartitionWrapper partition, CancellationToken cancellation)
        {
            var replicas = new List<DiscoveredReplica>();
            await foreach (var replica in this.queryClient.GetReplicasAsync(partition.PartitionId, Consts.DefaultFabricApiTimeout, cancellation))
            {
                var discoveredReplica = new DiscoveredReplica(replica);
                replicas.Add(discoveredReplica);
            }

            return replicas;
        }
    }
}
