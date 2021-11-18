// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Fabric.Query;

namespace Yarp.ServiceFabric.ServiceFabricIntegration
{
    /// <summary>
    /// Unit testable wrapper of a Service Fabric Service.
    /// </summary>
    public record ServiceWrapper
    {
        /// <summary>
        /// Service name.
        /// </summary>
        public ServiceNameKey ServiceName { get; init; }

        /// <summary>
        /// Service type name.
        /// </summary>
        public ServiceTypeNameKey ServiceTypeName { get; init; }

        /// <summary>
        /// Service manifest version.
        /// </summary>
        public string ServiceManifestVersion { get; init; }

        /// <summary>
        /// Service kind.
        /// </summary>
        public ServiceKind ServiceKind { get; init; }

        /// <summary>
        /// Service status.
        /// </summary>
        public ServiceStatus ServiceStatus { get; init; }
    }
}