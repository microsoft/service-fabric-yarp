// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Services.Runtime;
using Yarp.ServiceFabric.CoreServicesBorrowed.Extensions;

namespace Yarp.ServiceFabric.Service
{
    /// <summary>
    /// Entry point.
    /// </summary>
    internal static class Program
    {
        private static bool IsServiceFabric => Environment.GetEnvironmentVariable("Fabric_ApplicationName") != null;

        /// <summary>
        /// This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            // Opt-in to ASP .NET Core 3.1.5 routing fixes.
            // For more info, see https://github.com/dotnet/aspnetcore/pull/21200
            AppContext.SetSwitch("Microsoft.AspNetCore.Routing.UseCorrectCatchAllBehavior", true);

            if (IsServiceFabric)
            {
                FabricMain();
            }
            else
            {
                StandaloneMain();
            }
        }

        private static void FabricMain()
        {
            ILogger entrypointLogger = null;
            try
            {
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

                entrypointLogger = loggerFactory.CreateLogger("SFYarp.EntryPoint");
                entrypointLogger.LogInformation("SFYarp starting...");
                entrypointLogger.LogUnhandledExceptions(typeof(Program));

                ServiceRuntime.RegisterServiceAsync(
                    "YarpProxyType",
                    context => new SFYarpService(context, entrypointLogger)).GetAwaiter().GetResult();

                // Prevents this host process from terminating so services keep running.
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                entrypointLogger?.LogError(ex, "SFYarp startup failed.");
                throw;
            }
        }

        private static void StandaloneMain()
        {
            SFYarpStandalone.Run();
        }
    }
}
