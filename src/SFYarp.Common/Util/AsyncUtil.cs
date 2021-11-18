// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Yarp.ServiceFabric.Common
{
    /// <summary>
    /// Helper utility functions for dealing with async primitives.
    /// </summary>
    public static class AsyncUtil
    {
        /// <summary>
        /// Helper method to ignore uncooperative tasks on cancellation,
        /// while ensuring that the task does not go unobserved.
        /// There are 3 possible outcomes for this method:
        ///  1. Completes with a true value when <paramref name="task"/> succeeds within the allotted timeout.
        ///  2. Faults when <paramref name="task"/> faults within the allotted timeout.
        ///  3. Completes with a false value when <paramref name="task"/> does not complete within the allotted timeout.
        /// </summary>
        /// <remarks>
        /// This method should be used with care as it has non-obvious semantics.
        /// This method will produce a faulted task if the provided <paramref name="task"/> faults within the allotted timeout.
        /// But if the timeout expires, exceptions will be swallowed.
        /// </remarks>
        public static async Task<bool> IgnoreOnCancellation(Task task, CancellationToken cancellation)
        {
            // Task already completed (though it may be faulted) fast-path
            if (task.IsCompleted)
            {
                await task;
                return true;
            }

            if (await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cancellation)) == task)
            {
                // Task ran to completion, but may have faulted...
                await task;
                return true;
            }

            // Do not allow exceptions to go unobserved
            IgnoreExceptions(task);

            return false;

            static async void IgnoreExceptions(Task task)
            {
                try
                {
                    await task;
                }
                catch (Exception)
                {
                    // Swallow...
                }
            }
        }

        /// <summary>
        /// Attempts to execute an asynchronous action accepting a cancellation with the configured timeout.
        /// </summary>
        /// <returns>A value indicating whether exeuction completed prior to timeout.</returns>
        public static async Task<bool> ExecuteWithTimeout(Func<CancellationToken, Task> func, TimeSpan? timeout, CancellationToken cancellation)
        {
            CancellationTokenSource cts = null;
            var token = cancellation;
            try
            {
                if (timeout.HasValue)
                {
                    cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    cts.CancelAfter(timeout.Value);
                    token = cts.Token;
                }

                await func(token);
                return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellation.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                cts?.Dispose();
            }
        }
    }
}
