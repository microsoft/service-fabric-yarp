// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace IslandGateway.ServiceFabricIntegration
{
    /// <summary>
    /// Three modes for endpoint (replica) selection.
    /// See implementation in SF Reverse Proxy: <see href="https://msazure.visualstudio.com/One/_git/WindowsFabric?path=%2Fsrc%2Fprod%2Fsrc%2FManagement%2FApplicationGateway%2FHttp%2FTargetReplicaSelector.h&amp;version=GBdevelop&amp;line=14&amp;lineEnd=19&amp;lineStartColumn=1&amp;lineEndColumn=1&amp;lineStyle=plain"/>.
    /// </summary>
    public static class StatefulReplicaSelectionLabel
    {
        /// <summary>
        /// All replicas (Primary as well as Active Secondaries) of stateful services get traffic. This is the default option.
        /// </summary>
        public static readonly string All = "All";

        /// <summary>
        /// Only Primary replicas of stateful services get traffic.
        /// </summary>
        public static readonly string PrimaryOnly = "PrimaryOnly";

        /// <summary>
        /// Only Secondary replicas of stateful services get traffic.
        /// </summary>
        public static readonly string SecondaryOnly = "SecondaryOnly";
    }
}
