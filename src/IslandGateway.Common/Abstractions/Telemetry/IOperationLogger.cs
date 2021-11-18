// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Yarp.ServiceFabric.Common.Abstractions.Telemetry
{
    /// <summary>
    /// Provides methods to log telemetry for the execution of chunks of
    /// synchronous or asynchronous operations.
    /// </summary>
    public interface IOperationLogger
    {
        /// <summary>
        /// Gets the context for the current operation.
        /// </summary>
        IOperationContext Context { get; }

        /// <summary>
        /// Tracks the execution of the provided <paramref name="action"/>
        /// as an operation named <paramref name="operationName"/>.
        /// </summary>
        void Execute(string operationName, Action action, IEnumerable<KeyValuePair<string, string>> properties = null);

        /// <summary>
        /// Tracks the execution of the provided <paramref name="func"/>
        /// as an operation named <paramref name="operationName"/>.
        /// </summary>
        TResult Execute<TResult>(string operationName, Func<TResult> func, IEnumerable<KeyValuePair<string, string>> properties = null);

        /// <summary>
        /// Tracks the asynchronous execution of the provided <paramref name="func"/>
        /// as an operation named <paramref name="operationName"/>.
        /// </summary>
        Task ExecuteAsync(string operationName, Func<Task> func, IEnumerable<KeyValuePair<string, string>> properties = null);

        /// <summary>
        /// Tracks the asynchronous execution of the provided <paramref name="func"/>
        /// as an operation named <paramref name="operationName"/>.
        /// </summary>
        Task<TResult> ExecuteAsync<TResult>(string operationName, Func<Task<TResult>> func, IEnumerable<KeyValuePair<string, string>> properties = null);

        /// <summary>
        /// Tracks the execution of the provided <paramref name="action"/>
        /// as a root operation named <paramref name="operationName"/>.
        /// Root operations are created with a new correlation id.
        /// </summary>
        void ExecuteRoot(string operationName, Action action, IEnumerable<KeyValuePair<string, string>> properties = null);

        /// <summary>
        /// Tracks the asynchronous execution of the provided <paramref name="func"/>
        /// as a root operation named <paramref name="operationName"/>.
        /// Root operations are created with a new correlation id.
        /// </summary>
        public Task ExecuteRootAsync(string operationName, Func<Task> func, IEnumerable<KeyValuePair<string, string>> properties = null);
    }
}
