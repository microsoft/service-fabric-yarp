// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Yarp.ServiceFabric.RemoteConfig
{
    /// <summary>
    /// Options affecting SFYarp service discovery.
    /// </summary>
    public class RemoteConfigDiscoveryOptions
    {
        /// <summary>
        /// Semicolon-delimited list of Service Fabric service names that should be used for service discovery.
        /// The destination service is expected to honor the "SFYarp External Configuration Provider" API specs.
        /// </summary>
        public string ExternalDiscoveryServiceNames { get; set; }
    }
}
