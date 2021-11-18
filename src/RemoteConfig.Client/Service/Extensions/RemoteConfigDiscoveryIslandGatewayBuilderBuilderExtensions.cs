// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ServiceFabric.Common;
using Yarp.ServiceFabric.RemoteConfig.Fabric;
using Yarp.ServiceFabric.RemoteConfig.Metrics;

namespace Yarp.ServiceFabric.RemoteConfig
{
    /// <summary>
    /// Extensions used to register remote configuration components.
    /// </summary>
    public static class RemoteConfigDiscoveryIslandGatewayBuilderBuilderExtensions
    {
        /// <summary>
        /// Adds the services needed to use external (remote) configurations.
        /// </summary>
        public static IReverseProxyBuilder LoadFromRemoteConfigProvider(this IReverseProxyBuilder builder)
        {
            builder.Services.AddSingleton<IRemoteConfigFetcherFactory, SFRemoteConfigFetcherFactory>();
            builder.Services.AddSingleton<IProcessExiter, FailFastProcessExiter>();

            builder.Services.AddSingleton<RemoteConfigWorker>();
            builder.Services.AddSingleton<IProxyConfigProvider>(services => services.GetRequiredService<RemoteConfigWorker>());
            builder.Services.AddHostedService(services => services.GetRequiredService<RemoteConfigWorker>());
            builder.Services.AddSingleton<RemoteConfigMetrics>();

            return builder;
        }
    }
}
