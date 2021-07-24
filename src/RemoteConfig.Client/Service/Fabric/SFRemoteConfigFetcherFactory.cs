// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.RemoteConfig.Infra;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceFabric.Services.Client;

namespace IslandGateway.RemoteConfig.Fabric
{
    internal class SFRemoteConfigFetcherFactory : IRemoteConfigFetcherFactory
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly IOperationLogger operationLogger;
        private readonly IRemoteConfigClientFactory clientFactory;
        private readonly RemoteConfigDiscoveryOptions options;

        /// <summary>
        /// Initializes a new instance of the <see cref="SFRemoteConfigFetcherFactory"/> class.
        /// </summary>
        public SFRemoteConfigFetcherFactory(
            IRemoteConfigClientFactory clientFactory,
            IOptions<RemoteConfigDiscoveryOptions> options,
            ILoggerFactory loggerFactory,
            IOperationLogger operationLogger)
        {
            this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
        }

        public IRemoteConfigFetcher CreateFetcher()
        {
            var partitionResolver = ServicePartitionResolver.GetDefault();
            var httpClient = this.clientFactory.CreateClient();
            var fetchers = this.options.ExternalDiscoveryServiceNames?.Split(';', ',')
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrEmpty(e))
                .Select(serviceName =>
                {
                    var endpointResolver = new SFServiceEndpointResolver(new Uri(serviceName), partitionResolver, this.loggerFactory.CreateLogger<SFServiceEndpointResolver>());
                    var fetcher = new RemoteConfigFetcher(
                        endpointResolver,
                        httpClient,
                        this.loggerFactory.CreateLogger<RemoteConfigFetcher>(),
                        this.operationLogger);
                    return fetcher;
                })
                .ToArray();
            if (fetchers == null || fetchers.Length == 0)
            {
                throw new InvalidOperationException($"{nameof(this.options.ExternalDiscoveryServiceNames)} should specify at least one service name.");
            }

            return new RoundRobinConfigFetcher(fetchers);
        }
    }
}
