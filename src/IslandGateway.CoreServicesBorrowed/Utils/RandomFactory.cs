// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace IslandGateway.CoreServicesBorrowed
{
    /// <inheritdoc/>
    public class RandomFactory : IRandomFactory
    {
        /// <inheritdoc/>
        public IRandom CreateRandomInstance()
        {
            return ThreadStaticRandom.Instance;
        }
    }
}
