// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IslandGateway.FabricDiscovery.Util
{
    internal static class AsyncEnumerableExtensions
    {
        public static async Task<T> FirstOrDefaultAsync<T>(this IAsyncEnumerable<T> source, CancellationToken cancellation = default)
        {
            _ = source ?? throw new ArgumentNullException(nameof(source));
            await foreach (var item in source.WithCancellation(cancellation))
            {
                return item;
            }

            return default;
        }
    }
}
