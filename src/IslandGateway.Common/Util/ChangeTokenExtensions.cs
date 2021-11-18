// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;

namespace Yarp.ServiceFabric.Common
{
    /// <summary>
    /// Contains extensions related to <see cref="IChangeToken"/>.
    /// </summary>
    public static class ChangeTokenExtensions
    {
        /// <summary>
        /// Returns a task that completes when the <see cref="IChangeToken"/> changes or the provided cancellation token is cancelled.
        /// </summary>
        public static async Task WaitForChanges(this IChangeToken changeToken, CancellationToken cancellationToken)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (changeToken.RegisterChangeCallback(static state => ((CancellationTokenSource)state).Cancel(), cts))
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // This is successful case where the change token has now changed.
                }
            }
        }
    }
}
