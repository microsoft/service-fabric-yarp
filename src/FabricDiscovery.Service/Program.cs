// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Services.Runtime;

namespace IslandGateway.FabricDiscovery
{
    internal static class Program
    {
        private static bool IsServiceFabric => Environment.GetEnvironmentVariable("Fabric_ApplicationName") != null;

        /// <summary>
        /// This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            if (IsServiceFabric)
            {
                FabricMain();
            }
            else
            {
                StandaloneMain().GetAwaiter().GetResult();
            }
        }

        private static void FabricMain()
        {
            ILogger entrypointLogger = null;
            try
            {
                // NOTE: `TraceSourceMeta.LoggerFactory` is set to its correct value in `Startup.Configure`.
                // but since CoreFramework may want to log something before we get that far,
                // set up an interim logger factory, which will later be replaced. We use the same underlying LoggingProvider
                // in both cases, so the only difference between this and the one we will configure later
                // is that the one in `Startup.Configure` will be wired up with the rest of ASP .NET Core (e.g. log filtering etc.).
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

                entrypointLogger = loggerFactory.CreateLogger("FabricDiscoveryService.EntryPoint");
                entrypointLogger.LogInformation("FabricDiscoveryService starting...");

                ServiceRuntime.RegisterServiceAsync(
                    "FabricDiscoveryServiceType",
                    context => new StatefulServiceAdapter(
                        context,
                        loggerFactory.CreateLogger<StatefulServiceAdapter>(),
                        new TextOperationLogger(loggerFactory.CreateLogger<TextOperationLogger>()),
                        new FabricDiscoveryService(
                            entrypointLogger: entrypointLogger)))
                    .GetAwaiter().GetResult();

                // Prevents this host process from terminating so services keeps running.
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                entrypointLogger?.LogError(ex, "FabricDiscoveryService startup failed.");
                throw;
            }
        }

        private static async Task StandaloneMain()
        {
            var loggerFactory = LoggerFactory.Create(loggingBuilder =>
            {
                loggingBuilder.AddConsole();
            });

            var entrypointLogger = loggerFactory.CreateLogger("Entrypoint");
            var serviceWrapper = new FabricDiscoveryService(
                entrypointLogger: entrypointLogger,
                configureLogging: loggingBuilder =>
                {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.AddConsole();
                });
            var webHost = serviceWrapper.CreateWebHostBuilder().Build();

            using var cts = new CancellationTokenSource();
            var runAsyncTask = ExecuteRunAsync();
            var webhostTask = ExecuteWebHost();
            try
            {
                await Task.WhenAll(runAsyncTask, webhostTask);
            }
            catch (Exception ex)
            {
                entrypointLogger.LogError(ex, "Standalone execution failed.");
                throw;
            }

            async Task ExecuteRunAsync()
            {
                try
                {
                    await serviceWrapper.RunAsync(cts.Token);
                }
                catch
                {
                    cts.Cancel();
                    throw;
                }
            }

            async Task ExecuteWebHost()
            {
                try
                {
                    await webHost.RunAsync(cts.Token);
                }
                finally
                {
                    // When the webhost terminates, we terminate.
                    cts.Cancel();
                }
            }
        }
    }
}
