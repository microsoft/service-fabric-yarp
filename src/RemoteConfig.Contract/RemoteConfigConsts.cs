// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace IslandGateway.RemoteConfig.Contract
{
    /// <summary>
    /// Models the response for the config endpoint.
    /// </summary>
    public static class RemoteConfigConsts
    {
        /// <summary>
        /// Header that specifies the configuration version that was last seen.
        /// Helps detect changes.
        /// </summary>
        public static readonly string ETagHeader = "ETag";

        /// <summary>
        /// Header that specifies the configuration version that was last seen.
        /// Helps detect changes.
        /// </summary>
        public static readonly string IfNoneMatchHeader = "If-None-Match";

        /// <summary>
        /// Specifies how long to wait when there are no changes to the config.
        /// </summary>
        public static readonly string PollTimeoutHeader = "x-ms-poll-timeout";
    }
}
