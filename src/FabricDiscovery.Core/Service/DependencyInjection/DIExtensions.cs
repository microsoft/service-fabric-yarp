// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using IslandGateway.Common;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.Common.Abstractions.Time;
using IslandGateway.Common.Telemetry;
using IslandGateway.FabricDiscovery.FabricWrapper;
using IslandGateway.FabricDiscovery.IslandGatewayConfig;
using IslandGateway.FabricDiscovery.Topology;
using IslandGateway.FabricDiscovery.Util;
using IslandGateway.ServiceFabricIntegration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace IslandGateway.FabricDiscovery
{
    /// <summary>
    /// Extension methods to help with dependency injection.
    /// </summary>
    public static class DIExtensions
    {
        /// <summary>
        /// Adds components necessary to support Service Fabric service discovery for use with a YARP reverse proxy.
        /// </summary>
        public static IServiceCollection AddFabricDiscovery(this IServiceCollection services)
        {
            services.AddSingleton<IQueryClientWrapper, QueryClientWrapper>();
            services.AddSingleton<IPropertyManagementClientWrapper, PropertyManagementClientWrapper>();
            services.AddSingleton<IServiceManagementClientWrapper, ServiceManagementClientWrapper>();

            services.AddSingleton<IAppTypeDiscoveryManager, AppTypeDiscoveryManager>();
            services.AddSingleton<IServiceDiscoveryManager, ServiceDiscoveryManager>();
            services.AddSingleton<IServiceDiscoveryHelper, ServiceDiscoveryHelper>();
            services.AddSingleton<TopologyDiscoveryFilter, IslandGatewayTopologyDiscoveryFilter>();

            services.AddSingleton<IExtensionLabelsParser, ExtensionLabelsParser>();
            services.AddSingleton<IProcessExiter, FailFastProcessExiter>();
            services.AddSingleton<TopologyFreshnessTracker>();
            services.AddSingleton<IslandGatewayFreshnessTracker>();
            services.AddSingleton<FabricDiscoveryMetrics>();

            services.AddSingleton<TopologyDiscoveryWorker>();
            services.AddSingleton<ISnapshotProvider<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>>(services => services.GetRequiredService<TopologyDiscoveryWorker>());
            services.AddHostedService(services => services.GetRequiredService<TopologyDiscoveryWorker>());

            services.AddSingleton<IslandGatewayTopologyMapperWorker>();
            services.AddSingleton<ISnapshotProvider<IReadOnlyList<IslandGatewayBackendService>>>(services => services.GetRequiredService<IslandGatewayTopologyMapperWorker>());
            services.AddHostedService(services => services.GetRequiredService<IslandGatewayTopologyMapperWorker>());

            services.AddSingleton<IIslandGatewayConfigProducer, IslandGatewayConfigProducer>();
            services.AddSingleton<IslandGatewayConfigProducerWorker>();
            services.AddSingleton<IProxyConfigProvider>(services => services.GetRequiredService<IslandGatewayConfigProducerWorker>());
            services.AddHostedService(services => services.GetRequiredService<IslandGatewayConfigProducerWorker>());

            services.AddSingleton<IslandGatewayConfigSerializerWorker>();
            services.AddSingleton<ISnapshotProvider<IslandGatewaySerializedConfig>>(services => services.GetRequiredService<IslandGatewayConfigSerializerWorker>());
            services.AddHostedService(services => services.GetRequiredService<IslandGatewayConfigSerializerWorker>());

            services.AddHostedService<BackgroundMetricsWorker>();

            services.AddSingleton<IMetricCreator, NullMetricCreator>();
            services.AddSingleton<IMonotonicTimer, Common.Util.MonotonicTimer>();
            return services;
        }
    }
}
