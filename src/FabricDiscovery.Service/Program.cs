// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Services.Runtime;
using Yarp.ServiceFabric.Common.Telemetry;

namespace Yarp.ServiceFabric.FabricDiscovery
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
                // Service Fabric creates the adapter before the ASP.NET Core host, so use a separate logger factory
                // for entry-point and adapter lifecycle logging. The communication listener configures the web host's
                // logging pipeline when it opens.
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

                entrypointLogger = loggerFactory.CreateLogger("FabricDiscoveryService.EntryPoint");
                entrypointLogger.LogInformation("FabricDiscoveryService starting...");

                ServiceRuntime.RegisterServiceAsync(
                    "FabricDiscoveryServiceType",
                    context => new StatelessServiceAdapter(
                        context,
                        loggerFactory.CreateLogger<StatelessServiceAdapter>(),
                        new TextOperationLogger(loggerFactory.CreateLogger<TextOperationLogger>()),
                        new FabricDiscoveryService()))
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
                configureLogging: loggingBuilder =>
                {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.AddConsole();
                });
            var host = serviceWrapper.CreateWebHost();

            try
            {
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                entrypointLogger.LogError(ex, "Standalone execution failed.");
                throw;
            }
        }
    }
}
