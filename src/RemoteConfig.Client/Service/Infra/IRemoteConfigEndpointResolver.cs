// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace IslandGateway.RemoteConfig.Infra
{
    /// <summary>
    /// Interface for a class that resolves concrete uri's that can be used to reach the external configuration provider.
    /// </summary>
    internal interface IRemoteConfigEndpointResolver
    {
        /// <summary>
        /// Gets the next endpoint to try.
        /// </summary>
        Task<Uri> TryGetNextEndpoint(CancellationToken cancellation);
    }
}