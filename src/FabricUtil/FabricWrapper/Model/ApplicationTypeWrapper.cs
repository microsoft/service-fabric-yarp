// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Fabric.Query;

namespace IslandGateway.ServiceFabricIntegration
{
    /// <summary>
    /// Unit testable wrapper of a Service Fabric Application Type.
    /// </summary>
    public record ApplicationTypeWrapper
    {
        /// <summary>
        /// Application type name.
        /// </summary>
        public ApplicationTypeNameKey ApplicationTypeName { get; init; }

        /// <summary>
        /// Application type version.
        /// </summary>
        public ApplicationTypeVersionKey ApplicationTypeVersion { get; init; }

        /// <summary>
        /// Default application parameters.
        /// </summary>
        public IReadOnlyDictionary<string, string> DefaultParameters { get; init; }

        /// <summary>
        /// The application type status.
        /// </summary>
        public ApplicationTypeStatus Status { get; init; }
    }
}
