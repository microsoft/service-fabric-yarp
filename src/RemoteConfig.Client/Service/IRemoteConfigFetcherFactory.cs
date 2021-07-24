// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using IslandGateway.RemoteConfig.Infra;

namespace IslandGateway.RemoteConfig
{
    /// <summary>
    /// Provides a method to create a <see cref="IRemoteConfigFetcher"/> used to fetch remote configuration.
    /// </summary>
    internal interface IRemoteConfigFetcherFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="IRemoteConfigFetcher"/> used to fetch external configurations.
        /// </summary>
        IRemoteConfigFetcher CreateFetcher();
    }
}