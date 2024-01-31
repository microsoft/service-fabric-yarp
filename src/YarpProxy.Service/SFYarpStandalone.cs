// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Yarp.ServiceFabric.CoreServicesBorrowed;
using YarpProxy.Service.Lifecycle;

namespace Yarp.ServiceFabric.Service
{
    internal static class SFYarpStandalone
    {
        public static void Run()
        {
            // TODO: Wire-up with SigTerm (`Console.CancelKeyPress`)
            var shutdownStateManager = new ShutdownStateManager();

            // TODO: Support configurable endpoints to listen to, e.g. by reading command line config.
            var host = SFYarpService.CreateWebHost(
                shutdownStateManager: shutdownStateManager,
                urls: new[] { "http://+:280", "https://+:2443" },
                serviceContext: null,
                configureAppConfigurationAction: _ => { });
            host.Run();
        }
    }
}
