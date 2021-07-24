// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using IslandGateway.RemoteConfig.Contract;

namespace IslandGateway.RemoteConfig.Infra
{
    /// <summary>
    /// Invokes a set of <see cref="RemoteConfigFetcher"/>'s in a round-robin fashion.
    /// This helps improve reliability in case one or more of the multiple available destination services become unavailable.
    /// </summary>
    internal class RoundRobinConfigFetcher : IRemoteConfigFetcher
    {
        private readonly IRemoteConfigFetcher[] fetchers;
        private int nextIndex;

        public RoundRobinConfigFetcher(IRemoteConfigFetcher[] fetchers)
        {
            if (fetchers == null || fetchers.Length == 0)
            {
                throw new ArgumentException(nameof(fetchers));
            }

            this.fetchers = fetchers;
        }

        public IAsyncEnumerable<(RemoteConfigResponseDto Config, string ETag)> GetConfigurationStream(string lastSeenETag, CancellationToken cancellation)
        {
            var client = this.fetchers[this.nextIndex];
            this.nextIndex = (this.nextIndex + 1) % this.fetchers.Length;

            return client.GetConfigurationStream(lastSeenETag, cancellation);
        }
    }
}
