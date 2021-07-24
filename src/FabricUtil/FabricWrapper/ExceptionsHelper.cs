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
    public static class ExceptionsHelper
    {
        /// <summary>
        /// Translates Service Fabric's <see cref="FabricTransientException"/>
        /// into the appropriate <see cref="OperationCanceledException"/> when it represents a deliberate cancellation.
        /// </summary>
        /// <remarks>
        /// This overload does not provide a <c>state</c> argument and therefore leads to inefficient calling patterns that require
        /// allocations for lambda captures. Prefer the other overloads for new code.
        /// </remarks>
        public static async Task<TResult> TranslateCancellations<TResult>(Func<Task<TResult>> func, CancellationToken cancellation)
        {
            try
            {
                return await func();
            }
            catch (FabricTransientException ex)
            {
                if (ex.ErrorCode == FabricErrorCode.OperationCanceled && cancellation.IsCancellationRequested)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Execution should never get here...");
                }

                throw;
            }
        }

        /// <summary>
        /// Translates Service Fabric's <see cref="FabricTransientException"/>
        /// into the appropriate <see cref="OperationCanceledException"/> when it represents a deliberate cancellation.
        /// </summary>
        public static async Task<TResult> TranslateCancellations<TResult, TState>(Func<TState, Task<TResult>> func, TState state, CancellationToken cancellation)
        {
            try
            {
                return await func(state);
            }
            catch (FabricTransientException ex)
            {
                if (ex.ErrorCode == FabricErrorCode.OperationCanceled && cancellation.IsCancellationRequested)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Execution should never get here...");
                }

                throw;
            }
        }

        /// <summary>
        /// Translates Service Fabric's <see cref="FabricTransientException"/>
        /// into the appropriate <see cref="OperationCanceledException"/> when it represents a deliberate cancellation.
        /// </summary>
        public static async Task TranslateCancellations<TState>(Func<TState, Task> func, TState state, CancellationToken cancellation)
        {
            try
            {
                await func(state);
            }
            catch (FabricTransientException ex)
            {
                if (ex.ErrorCode == FabricErrorCode.OperationCanceled && cancellation.IsCancellationRequested)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Execution should never get here...");
                }

                throw;
            }
        }
    }
}
