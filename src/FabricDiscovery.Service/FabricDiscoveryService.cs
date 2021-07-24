// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.Common.Abstractions.Time;
using IslandGateway.Common.Telemetry;
using IslandGateway.FabricDiscovery.FabricWrapper;
using IslandGateway.FabricDiscovery.IslandGatewayConfig;
using IslandGateway.FabricDiscovery.Topology;
using IslandGateway.FabricDiscovery.Util;
using IslandGateway.ServiceFabricIntegration;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace IslandGateway.FabricDiscovery
{
    /// <summary>
    /// Logical entry-point of Island Gateway's Fabric Discovery service.
    /// Control is transferred to methods in this class after the service is bootstrapped and selected as Primary.
    /// </summary>
    /// <remarks>
    /// Service Fabric Discovery can be thought of as an asynchronous pipeline that operates on state changes (PUSH model),
    /// although specific parts of the pipeline use polling to detect changes. The high level flow is managed by a few
    /// <see cref="CriticalBackgroundService"/>'s:
    /// <list type="number">
    ///   <item>
    ///     <see cref="TopologyDiscoveryWorker"/> manages discovery of the Service Fabric topology,
    ///     and it implements a combination of PUSH + PULL. It polls Service Fabric periodically to refresh the entire topology,
    ///     but it also reacts to Service Fabric endpoint change notifications to quickly detect replica movement.
    ///   </item>
    ///   <item>
    ///     <see cref="IslandGatewayTopologyMapperWorker"/> transforms the raw Service Fabric topology into something that
    ///     is more directly applicable to producing Island Gateway configs (e.g. it pre-parses Service Manifest Extensions labels).
    ///   </item>
    ///   <item>
    ///     <see cref="IslandGatewayConfigProducerWorker"/> queries Service Fabric Properties
    ///     for services that have opted-in to dynamic overrides, and produces the final Island Gateway configs when there are changes.
    ///     This also implements a debouncing logic to ensure we are not spending too much time updating configs unnecessarily.
    ///   </item>
    ///   <item>
    ///     <see cref="BackgroundMetricsWorker"/> emits metrics about how the pipeline is performing.
    ///   </item>
    /// </list>
    /// Ordering of the steps above is critical, and we require successful initialization of this pipeline
    /// (which includes producing the first complete Island Gateway configuration). If any step fails during initialization,
    /// the entire service is brought down.
    /// Subsequently, each pipeline stage configures their own thresholds for early termination, and the service commits suicide
    /// if a stage is unable to make progress. This can help e.g. when a single machine in a Service Fabric Cluster is experiencing
    /// connectivity issues that prevent successful Service Discovery. Killing the current process gives a chance for Service Fabric
    /// to start us on a different machine which will hopefully work correctly.
    /// </remarks>
    internal class FabricDiscoveryService : IStatefulServiceWrapper
    {
        private readonly DIAdapter diAdapter = new DIAdapter();
        private readonly ILogger entrypointLogger;
        private readonly Action<ILoggingBuilder> configureLogging;

        /// <summary>
        /// Initializes a new instance of the <see cref="FabricDiscoveryService"/> class.
        /// </summary>
        public FabricDiscoveryService(ILogger entrypointLogger, Action<ILoggingBuilder> configureLogging = null)
        {
            this.entrypointLogger = entrypointLogger ?? throw new ArgumentNullException(nameof(entrypointLogger));
            this.configureLogging = configureLogging ?? (_ => { });
        }

        /// <inheritdoc/>
        public IWebHostBuilder CreateWebHostBuilder()
        {
            return WebHost.CreateDefaultBuilder()
                ////.UseKestrelWithIntraCommCertificate()
                .UseContentRoot(Directory.GetCurrentDirectory())
                ////.UseCoreServicesConfiguration(this.entrypointLogger, new string[0])
                .ConfigureLogging(this.configureLogging)
                .ConfigureServices((context, services) =>
                {
                    ConfigureCommonServices(context.Configuration, services);
                    services.AddSingleton(this.diAdapter);
                })
                .UseStartup<Startup>();
        }

        /// <inheritdoc/>
        public async Task RunAsync(CancellationToken cancellation)
        {
            this.entrypointLogger.LogInformation($"{nameof(FabricDiscoveryService)}.{nameof(this.RunAsync)} is starting...");

            var host = this.CreateHostBuilder().Build();

            // Allow the WebHost to use classes that are registered and managed by `host`.
            this.diAdapter.SetServiceProvider(host.Services);

            await host.RunAsync(cancellation);
        }

        /// <summary>
        /// We maintain two separate Hosts.
        /// One is a WebHost, created and managed by our listener lifecycle.
        /// The other is a .NET Extensions vanilla Host, used to help with Dependency Injection and managing <see cref="IHostedService"/>'s
        /// for doing our primary work within <see cref="Microsoft.ServiceFabric.Services.Runtime.StatefulServiceBase.RunAsync"/>.
        /// This method configures services that are common to both.
        /// </summary>
        private static void ConfigureCommonServices(IConfiguration config, IServiceCollection services)
        {
            services.Configure<FabricDiscoveryOptions>(config.GetSection("FabricDiscovery"));
        }

        /// <summary>
        /// Creates a .NET Generic Host (see <see href="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-3.1"/>
        /// that manages the lifecycle of this replica while it is in a Primary role.
        /// Note that this is NOT an ASP .NET Core hot, and it does not expose any API endpoints.
        /// Rather, this simply manages a Dependency Injection container and lifecycle of supporting services,
        /// in particular through <see cref="IHostedService"/>'s that we register here.
        /// </summary>
        private IHostBuilder CreateHostBuilder()
        {
            return Host.CreateDefaultBuilder()
                .ConfigureLogging(this.configureLogging)
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IOperationLogger, TextOperationLogger>();
                    services.AddFabricDiscovery();

                    ConfigureCommonServices(context.Configuration, services);
                });
        }
    }
}
