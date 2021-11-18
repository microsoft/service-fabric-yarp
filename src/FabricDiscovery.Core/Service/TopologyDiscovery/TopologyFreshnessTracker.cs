// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Yarp.ServiceFabric.FabricDiscovery.Util
{
    internal class TopologyFreshnessTracker
    {
        /// <summary>
        /// Tracks how much time has passed since the list of dirty services was last refreshed.
        /// We expect the value to stay around 1 second, and if it ever grows much larger than that,
        /// it would indicate that we or Service Fabric api's may be running slow.
        /// </summary>
        public FreshnessTracker Topology { get; } = new FreshnessTracker();

        /// <summary>
        /// Tracks how much time has passed since the entire Service Fabric topology was refreshed.
        /// Since we periodically refresh the full topology, we expect this value to grow steadily
        /// up to the full refresh interval <see cref="FabricDiscoveryOptions.FullRefreshPollPeriodInSeconds"/>
        /// (say, 5 minutes), then to come back to zero as soon as the next refresh completes.
        /// If the value ever grows much larger than the configure full refresh interval,
        /// it would indicate that we or Service Fabric API's may be running slow.
        /// </summary>
        public FreshnessTracker FullTopology { get; } = new FreshnessTracker();
    }
}
