// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Yarp.ServiceFabric.FabricDiscovery
{
    /// <summary>
    /// Options that influence Service Fabric dynamic service discovery.
    /// </summary>
    public sealed class FabricDiscoveryOptions
    {
        /// <summary>
        /// Whether to allow Island Gateway to discover non-https endpoints.
        /// Defaults to <c>false</c>.
        /// </summary>
        // TODO: Make defaults more secure
        public bool AllowInsecureHttp { get; set; } = true;

        /// <summary>
        /// Terminate the primary if Service Fabric topology discovery is taking longer than this amount.
        /// This is done in the hope that another instance will carry on as Primary and may have better luck.
        /// </summary>
        public int AbortAfterTimeoutInSeconds { get; set; } = 600;

        /// <summary>
        /// Terminate the primary after it encounters this number of consecutive failures.
        /// This is done in the hope that another instance will carry on as Primary and may have better luck.
        /// </summary>
        public int AbortAfterConsecutiveFailures { get; set; } = 3;

        /// <summary>
        /// How long to wait between complete refreshes of the Service Fabric topology, in seconds.
        /// </summary>
        public int FullRefreshPollPeriodInSeconds { get; set; } = 300;
    }
}