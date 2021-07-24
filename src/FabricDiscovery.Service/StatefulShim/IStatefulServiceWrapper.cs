// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;

namespace IslandGateway.FabricDiscovery
{
    /// <summary>
    /// Wrapper for a stateful Service Fabric service.
    /// </summary>
    internal interface IStatefulServiceWrapper
    {
        /// <summary>
        /// Creates a new <see cref="IWebHostBuilder"/> used to create the listener for this service.
        /// This method can be called multiple times (e.g. each time this replica is promoted to primary).
        /// Any state that must be preserved across Primary / secondary promotions/demotions,
        /// should be kept outside the web host DI container.
        /// </summary>
        IWebHostBuilder CreateWebHostBuilder();

        /// <summary>
        /// Runs when primary.
        /// This method can be called multiple times (e.g. each time this replica is promoted to primary).
        /// </summary>
        Task RunAsync(CancellationToken cancellation);
    }
}
