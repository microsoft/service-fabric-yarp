// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Fabric.Description;

namespace Yarp.ServiceFabric.ServiceFabricIntegration
{
    /// <summary>
    /// Unit testable wrapper of a Service Fabric Service Type.
    /// </summary>
    public record ServiceTypeWrapper
    {
        /// <summary>
        /// Service Manifest name.
        /// </summary>
        public string ServiceManifestName { get; init; }

        /// <summary>
        /// Service Manifest version.
        /// </summary>
        public string ServiceManifestVersion { get; init; }

        /// <summary>
        /// Service type name.
        /// </summary>
        public ServiceTypeNameKey ServiceTypeName { get; init; }

        /// <summary>
        /// ServiceTypeKind.
        /// </summary>
        public ServiceDescriptionKind ServiceTypeKind { get; init; }

        /// <summary>
        /// Service type extensions.
        /// </summary>
        public IDictionary<string, string> Extensions { get; init; }
    }
}
