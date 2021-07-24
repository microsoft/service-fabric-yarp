// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using IslandGateway.CoreServicesBorrowed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using YarpProxy.Service.Lifecycle;

namespace Microsoft.PowerPlatform.CoreServices.IslandGateway
{
    internal static class IslandGatewayStandalone
    {
        public static void Run()
        {
            // TODO: Wire-up with SigTerm (`Console.CancelKeyPress`)
            var shutdownStateManager = new ShutdownStateManager();

            // TODO: Support configurable endpoints to listen to, e.g. by reading command line config.
            var host = IslandGatewayService.CreateWebHost(
                shutdownStateManager: shutdownStateManager,
                urls: new[] { "http://+:280", "https://+:2443" },
                configureAppConfigurationAction: _ => { });
            host.Run();
        }
    }
}
