// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.Common.Abstractions.Time;
using IslandGateway.Common.Telemetry;
using IslandGateway.Common.Util;
using IslandGateway.Core.Abstractions;
using IslandGateway.Core.Service.Security.ServerCertificateBinding;
using IslandGateway.Hosting.Common;
using IslandGateway.RemoteConfig;
using IslandGateway.RemoteConfig.Infra;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YarpProxy.Service.Lifecycle;

namespace Microsoft.PowerPlatform.CoreServices.IslandGateway
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
