// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ServiceFabric.Common;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Abstractions.Time;
using Yarp.ServiceFabric.Common.Telemetry;
using Yarp.ServiceFabric.FabricDiscovery.FabricWrapper;
using Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig;
using Yarp.ServiceFabric.FabricDiscovery.Topology;
using Yarp.ServiceFabric.FabricDiscovery.Util;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery
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
            services.AddSingleton<TopologyDiscoveryFilter, SFYarpTopologyDiscoveryFilter>();

            services.AddSingleton<IExtensionLabelsParser, ExtensionLabelsParser>();
            services.AddSingleton<IProcessExiter, FailFastProcessExiter>();
            services.AddSingleton<TopologyFreshnessTracker>();
            services.AddSingleton<SFYarpFreshnessTracker>();
            services.AddSingleton<FabricDiscoveryMetrics>();

            services.AddSingleton<TopologyDiscoveryWorker>();
            services.AddSingleton<ISnapshotProvider<IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>>>(services => services.GetRequiredService<TopologyDiscoveryWorker>());
            services.AddHostedService(services => services.GetRequiredService<TopologyDiscoveryWorker>());

            services.AddSingleton<SFYarpTopologyMapperWorker>();
            services.AddSingleton<ISnapshotProvider<IReadOnlyList<SFYarpBackendService>>>(services => services.GetRequiredService<SFYarpTopologyMapperWorker>());
            services.AddHostedService(services => services.GetRequiredService<SFYarpTopologyMapperWorker>());

            services.AddSingleton<ISFYarpConfigProducer, SFYarpConfigProducer>();
            services.AddSingleton<SFYarpConfigProducerWorker>();
            services.AddSingleton<IProxyConfigProvider>(services => services.GetRequiredService<SFYarpConfigProducerWorker>());
            services.AddHostedService(services => services.GetRequiredService<SFYarpConfigProducerWorker>());

            services.AddSingleton<SFYarpConfigSerializerWorker>();
            services.AddSingleton<ISnapshotProvider<SFYarpSerializedConfig>>(services => services.GetRequiredService<SFYarpConfigSerializerWorker>());
            services.AddHostedService(services => services.GetRequiredService<SFYarpConfigSerializerWorker>());

            services.AddHostedService<BackgroundMetricsWorker>();

            services.AddSingleton<IMetricCreator, NullMetricCreator>();
            services.AddSingleton<IMonotonicTimer, Common.Util.MonotonicTimer>();
            return services;
        }
    }
}
