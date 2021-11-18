// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Yarp.ServiceFabric.CoreServicesBorrowed
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
