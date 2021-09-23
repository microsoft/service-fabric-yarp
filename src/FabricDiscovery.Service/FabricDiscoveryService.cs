// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.Common.Telemetry;
using IslandGateway.FabricDiscovery.IslandGatewayConfig;
using IslandGateway.FabricDiscovery.Topology;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    internal class FabricDiscoveryService : IStatelessServiceWrapper
    {
        private readonly Action<ILoggingBuilder> configureLogging;

        /// <summary>
        /// Initializes a new instance of the <see cref="FabricDiscoveryService"/> class.
        /// </summary>
        public FabricDiscoveryService(Action<ILoggingBuilder> configureLogging = null)
        {
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
                    services.AddSingleton<IOperationLogger, TextOperationLogger>();
                    services.Configure<FabricDiscoveryOptions>(context.Configuration.GetSection("FabricDiscovery"));
                    services.AddFabricDiscovery();
                })
                .UseStartup<Startup>();
        }

        /// <inheritdoc/>
        public Task RunAsync(CancellationToken cancellation) => Task.CompletedTask;
    }
}
