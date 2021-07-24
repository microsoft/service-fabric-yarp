// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace IslandGateway.FabricDiscovery
{
    /// <summary>
    /// Dependency injection adapter to allow sharing of services between multiple Hosts / WebHosts.
    /// </summary>
    public sealed class DIAdapter
    {
        private IServiceProvider serviceProvider;

        /// <summary>
        /// Sets the source service provider from which instances will be queried via <see cref="GetService{T}"/>.
        /// </summary>
        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            _ = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            if (Interlocked.CompareExchange(ref this.serviceProvider, serviceProvider, null) != null)
            {
                throw new InvalidOperationException($"{nameof(this.SetServiceProvider)} can only be called once.");
            }
        }

        /// <summary>
        /// Attempts to get an instance of the provided type <typeparamref name="T"/>
        /// from the service provider previously registered via <see cref="SetServiceProvider"/>.
        /// </summary>
        public T GetService<T>()
            where T : class
        {
            return this.serviceProvider?.GetService<T>();
        }
    }
}
