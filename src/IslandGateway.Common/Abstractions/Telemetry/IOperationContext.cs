﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace IslandGateway.Common.Abstractions.Telemetry
{
    /// <summary>
    /// Provides contextual information for an ongoing operation.
    /// Operation contexts support nesting, and the current context
    /// can be obtained from <see cref="IOperationLogger.Context"/>.
    /// </summary>
    public interface IOperationContext
    {
        /// <summary>
        /// Sets a property on the current operation context.
        /// </summary>
        /// <param name="key">Property key.</param>
        /// <param name="value">Property value.</param>
        void SetProperty(string key, string value);

        /// <summary>
        /// Marks the operation as failed.
        /// </summary>
        void FailWith(Exception ex);
    }
}
