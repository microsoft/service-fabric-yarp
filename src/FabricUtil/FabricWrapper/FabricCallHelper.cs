// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;

namespace IslandGateway.ServiceFabricIntegration
{
    /// <summary>
    /// Helps translate Service Fabric aborted exceptions to standard types.
    /// </summary>
    public static class FabricCallHelper
    {
        /// <summary>
        /// Executes the provided <paramref name="func"/> and retries according to the specified <paramref name="retryPolicy"/> on transient errors,
        /// also taking care of translating deliberate cancellation exceptions.
        /// </summary>
        /// <param name="func">Function to be invoked with retries. The first argument specifies the current attempt number, starting from 1.</param>
        /// <param name="retryPolicy">The retry policy to follow.</param>
        /// <param name="cancellation">Cancellation token.</param>
        public static async Task<TResult> RunWithExponentialRetries<TResult>(Func<int, CancellationToken, Task<TResult>> func, FabricExponentialRetryPolicy retryPolicy, CancellationToken cancellation)
        {
            int attempt = 0;
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();

                attempt++;
                try
                {
                    return await func(attempt, cancellation);
                }
                catch (Exception ex) when (ex is FabricTransientException || ex is TimeoutException)
                {
                    if (ex is FabricTransientException transientException &&
                        transientException.ErrorCode == FabricErrorCode.OperationCanceled &&
                        cancellation.IsCancellationRequested)
                    {
                        // Explicit cancellation requested by the called...
                        cancellation.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("Execution should never get here...");
                    }

                    if (retryPolicy.IsRetryAllowed(attempt, out int backoffBeforeNextAttempt))
                    {
                        await Task.Delay(backoffBeforeNextAttempt, cancellation);
                        continue;
                    }

                    throw;
                }
            }
        }
    }
}
