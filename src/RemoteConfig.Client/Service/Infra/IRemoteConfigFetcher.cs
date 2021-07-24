// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using IslandGateway.RemoteConfig.Contract;

namespace IslandGateway.RemoteConfig.Infra
{
    /// <summary>
    /// Provides a method to fetch configurations from an external provider.
    /// </summary>
    internal interface IRemoteConfigFetcher
    {
        /// <summary>
        /// Gets configurations from an external providers, including real-time updates.
        /// </summary>
        /// <remarks>
        /// The stream of real-time updates may end (gracefully or otherwise) for a variety of reasons,
        /// in which case this method should be called again. Under the covers, the fetcher is going to try a different endpoint each time.
        /// </remarks>
        IAsyncEnumerable<(RemoteConfigResponseDto Config, string ETag)> GetConfigurationStream(string lastSeenETag, CancellationToken cancellation);
    }
}
