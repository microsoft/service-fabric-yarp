// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;

namespace Yarp.ServiceFabric.Common.Telemetry
{
    /// <summary>
    /// Implementation of <see cref="IOperationLogger"/>
    /// which doesn't log anything.
    /// </summary>
    public class NullOperationLogger : IOperationLogger
    {
        /// <inheritdoc/>
        public IOperationContext Context => new NullOperationContext();

        /// <inheritdoc/>
        public void Execute(string operationName, Action action, IEnumerable<KeyValuePair<string, string>> properties = null)
        {
            action();
        }

        /// <inheritdoc/>
        public TResult Execute<TResult>(string operationName, Func<TResult> func, IEnumerable<KeyValuePair<string, string>> properties = null)
        {
            return func();
        }

        /// <inheritdoc/>
        public async Task ExecuteAsync(string operationName, Func<Task> func, IEnumerable<KeyValuePair<string, string>> properties = null)
        {
            await func();
        }

        /// <inheritdoc/>
        public async Task<TResult> ExecuteAsync<TResult>(string operationName, Func<Task<TResult>> func, IEnumerable<KeyValuePair<string, string>> properties = null)
        {
            return await func();
        }

        /// <inheritdoc/>
        public void ExecuteRoot(string operationName, Action action, IEnumerable<KeyValuePair<string, string>> properties = null)
        {
            action();
        }

        /// <inheritdoc/>
        public async Task ExecuteRootAsync(string operationName, Func<Task> func, IEnumerable<KeyValuePair<string, string>> properties = null)
        {
            await func();
        }
    }
}
