// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Abstractions.Time;
using Yarp.ServiceFabric.Common.Telemetry;
using Yarp.ServiceFabric.Common.Util;
using Yarp.ServiceFabric.Core.Abstractions;
using Yarp.ServiceFabric.Core.Service.Security.ServerCertificateBinding;
using Yarp.ServiceFabric.Hosting.Common;
using Yarp.ServiceFabric.RemoteConfig;
using Yarp.ServiceFabric.RemoteConfig.Infra;
using YarpProxy.Service.Lifecycle;

namespace Yarp.ServiceFabric.Service
{
    /// <summary>
    /// The service startup configuration class.
    /// </summary>
    public class Startup
    {
        private readonly IConfiguration configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="Startup"/> class.
        /// </summary>
        public Startup(IConfiguration configuration)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// This method gets called by the runtime. Use this method to add services to the container.
        /// </summary>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRouting();

            services.AddSingleton<IMonotonicTimer, MonotonicTimer>();
            services.AddSingleton<IOperationLogger, TextOperationLogger>();
            services.AddSingleton<IMetricCreator, NullMetricCreator>();
            services.AddReverseProxy()
                .LoadFromRemoteConfigProvider();
            services.AddSingleton<IRemoteConfigClientFactory, RemoteConfigClientFactory>();

            services.AddSingleton<ICertificateLoader, CertificateLoader>();
            services.AddSingleton<ISniServerCertificateSelector, SniServerCertificateSelector>();
            services.AddHostedService<SniServerCertificateUpdater>();
            services.TryAddSingleton<ShutdownStateManager>();
            services.Configure<RemoteConfigDiscoveryOptions>(this.configuration.GetSection("RemoteConfigDiscovery"));
        }

        /// <summary>
        /// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        /// </summary>
        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapReverseProxy();
            });
        }
    }
}
