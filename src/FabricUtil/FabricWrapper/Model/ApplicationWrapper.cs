// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace IslandGateway.ServiceFabricIntegration
{
    /// <summary>
    /// Unit testable wrapper of a Service Fabric Application.
    /// </summary>
    public record ApplicationWrapper
    {
        /// <summary>
        /// Application name.
        /// </summary>
        public ApplicationNameKey ApplicationName { get; init; }

        /// <summary>
        /// Application type name.
        /// </summary>
        public ApplicationTypeNameKey ApplicationTypeName { get; init; }

        /// <summary>
        /// Application type version.
        /// </summary>
        public ApplicationTypeVersionKey ApplicationTypeVersion { get; init; }

        /// <summary>
        /// Application parameters.
        /// </summary>
        public IReadOnlyDictionary<string, string> ApplicationParameters { get; init; }
    }
}
