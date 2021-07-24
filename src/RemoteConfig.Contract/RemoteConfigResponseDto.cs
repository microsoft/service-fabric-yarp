// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Yarp.ReverseProxy.Configuration;

namespace IslandGateway.RemoteConfig.Contract
{
    /// <summary>
    /// Models the response for the config endpoint.
    /// </summary>
    public class RemoteConfigResponseDto
    {
        /// <summary>
        /// YARP clusters.
        /// </summary>
        public IReadOnlyList<ClusterConfig> Clusters { get; set; }

        /// <summary>
        /// YARP routes.
        /// </summary>
        public IReadOnlyList<RouteConfig> Routes { get; set; }

        /// <summary>
        /// Timestamp from when this configuration was produced.
        /// </summary>
        public DateTimeOffset AsOf { get; set; }
    }
}
