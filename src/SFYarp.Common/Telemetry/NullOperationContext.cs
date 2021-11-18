// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;

namespace Yarp.ServiceFabric.Common.Telemetry
{
    /// <summary>
    /// Implementation of <see cref="IOperationContext"/>
    /// which doesn't do anything.
    /// </summary>
    public class NullOperationContext : IOperationContext
    {
        /// <inheritdoc/>
        public void SetProperty(string key, string value)
        {
        }

        /// <inheritdoc/>
        public void FailWith(Exception ex)
        {
        }
    }
}
