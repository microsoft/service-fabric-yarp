// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Abstractions.Time;
using Yarp.ServiceFabric.Common.Telemetry;
using Yarp.ServiceFabric.Common.Util;
using Yarp.ServiceFabric.Core.Abstractions;
using Yarp.ServiceFabric.Core.Service.Security.ServerCertificateBinding;
using Yarp.ServiceFabric.CoreServicesBorrowed.CoreFramework;
using Yarp.ServiceFabric.Hosting.Common;
using Yarp.ServiceFabric.InternalTelemetry;
using Yarp.ServiceFabric.RemoteConfig;
using Yarp.ServiceFabric.RemoteConfig.Infra;
using YarpProxy.Service.Lifecycle;

namespace Yarp.ServiceFabric.Service
{
    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal class SFYarpService : StatelessService
    {
        private const long MaxRequestBodySize = 100 * 1024 * 1024;
        private const int HealthEndpointReactionTimeSeconds = 15;
        private const int DrainTimeSeconds = 60 * 3;
        private readonly ILogger entrypointLogger;
        private readonly ShutdownStateManager shutdownStateManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SFYarpService" /> class.
        /// </summary>
        /// <param name="context">Service Fabric service activation context.</param>
        /// <param name="entrypointLogger">Logger used to log messages during startup.</param>
        public SFYarpService(StatelessServiceContext context, ILogger entrypointLogger)
            : base(context)
        {
            Contracts.CheckValue(entrypointLogger, nameof(entrypointLogger));
            this.entrypointLogger = entrypointLogger;
            this.shutdownStateManager = new ShutdownStateManager();
        }

        internal static IHost CreateWebHost(
            ShutdownStateManager shutdownStateManager,
            string[] urls,
            StatelessServiceContext serviceContext,
            Action<IConfigurationBuilder> configureAppConfigurationAction)
        {
            Contracts.CheckValue(urls, nameof(urls));
            Contracts.Check(urls.Length > 0, nameof(urls));
            Contracts.CheckValue(configureAppConfigurationAction, nameof(configureAppConfigurationAction));

            // Declare a dummy cert selector at first. We can only get the actual cert selector instance
            // after the host is initialized, because that is what also sets up our Dependency Injection container.
            // Once the host is ready, we will replace this with a delegate to the actual cert selector function.
            Func<ConnectionContext, string, X509Certificate2> certSelectorFunc = (connectionContext, hostName) => null;

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel(
                kestrelOptions =>
                {
                    kestrelOptions.Limits.MaxRequestBodySize = MaxRequestBodySize;
                    kestrelOptions.ConfigureHttpsDefaults(
                        httpsOptions =>
                        {
                            httpsOptions.SslProtocols = SslProtocols.None;
                            httpsOptions.ServerCertificateSelector = (connectionContext, hostName) => certSelectorFunc(connectionContext, hostName);
                        });
                });

            builder.WebHost.ConfigureAppConfiguration(configureAppConfigurationAction)
                .UseUrls(urls)
                .UseShutdownTimeout(TimeSpan.FromSeconds(DrainTimeSeconds));

            builder.Services.AddRouting()
                .AddReverseProxy()
                    .LoadFromRemoteConfigProvider()
                    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

            builder.Services.AddSingleton<IRemoteConfigClientFactory, RemoteConfigClientFactory>()
                .Configure<RemoteConfigDiscoveryOptions>(builder.Configuration.GetSection("RemoteConfigDiscovery"))
                .AddSingleton(shutdownStateManager)
                .AddSingleton(serviceContext);

            builder.Services.AddHostedService<TelemetryManager>()
                .AddSingleton<IMonotonicTimer, MonotonicTimer>()
                .AddSingleton<IOperationLogger, TextOperationLogger>()
                .AddSingleton<IMetricCreator, NullMetricCreator>();

            builder.Services.AddSingleton<ICertificateLoader, CertificateLoader>()
                .AddSingleton<ISniServerCertificateSelector, SniServerCertificateSelector>()
                .AddHostedService<SniServerCertificateUpdater>()
                .AddHttpLogging(logging =>
                {
                    logging.LoggingFields = HttpLoggingFields.All;
                });

            var options = new ApplicationInsightsServiceOptions { ConnectionString = builder.Configuration.GetConnectionString("ApplicationInsights") };
            builder.Services.AddApplicationInsightsTelemetry(options: options);

            builder.Logging.AddFilter<ApplicationInsightsLoggerProvider>("Microsoft.AspNetCore", LogLevel.Trace);
            builder.Logging.AddFilter<ApplicationInsightsLoggerProvider>("Yarp", LogLevel.Trace);
            var app = builder.Build();

            app.UseHttpLogging()
               .UseRouting()
               .UseCors()
               .UseEndpoints(endpoints => { endpoints.MapReverseProxy(); });

            var certSelector = app.Services.GetRequiredService<ISniServerCertificateSelector>();
            certSelectorFunc = certSelector.SelectCertificate;
            return app;
        }

        /// <inheritdoc/>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[]
            {
                new ServiceInstanceListener(serviceContext =>
                    new DeferredCloseCommunicationListener(
                        new KestrelCommunicationListener(serviceContext, "HttpServiceEndpoint", (_, listener) =>
                        {
                            var httpUrl = GetEndpointUrl(serviceContext, "HttpServiceEndpoint");
                            var httpsUrl = GetEndpointUrl(serviceContext, "HttpsServiceEndpoint");
                            var urls = new[] { httpUrl, httpsUrl };

                            this.entrypointLogger.LogInformation($"Starting Kestrel on {string.Join(", ", urls)}");
                            var host = CreateWebHost(
                                shutdownStateManager: this.shutdownStateManager,
                                urls: urls,
                                serviceContext: serviceContext,
                                configureAppConfigurationAction: configBuilder =>
                                {
                                    // NOTE: `WebHost.CreateDefaultBuilder` has already added the default configuration sources at this point
                                    // (appsettings.json, appsettings.ENVIRONMENT.json).
                                    // The next line adds Service Fabric configs last, so SF service configs will take precedence over appsettings.json
                                    ////configBuilder.AddServiceFabricConfiguration();
                                });

                            return host;
                        }),
                        delay: TimeSpan.FromSeconds(HealthEndpointReactionTimeSeconds),
                        shutdownStateManager: this.shutdownStateManager,
                        logger: this.entrypointLogger)),
            };

            static string GetEndpointUrl(StatelessServiceContext serviceContext, string endpointName)
            {
                Contracts.CheckValue(serviceContext, nameof(serviceContext));
                Contracts.CheckNonEmpty(endpointName, nameof(endpointName));

                if (!serviceContext.CodePackageActivationContext.GetEndpoints().Contains(endpointName))
                {
                    throw new InvalidOperationException($"Initialization failed, no endpoint named '{endpointName}'.");
                }

                var endpoint = serviceContext.CodePackageActivationContext.GetEndpoint(endpointName);
                return $"{endpoint.UriScheme}://+:{endpoint.Port}";
            }
        }
    }
}