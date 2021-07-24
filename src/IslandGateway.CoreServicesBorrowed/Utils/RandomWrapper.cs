﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using IslandGateway.CoreServicesBorrowed.CoreFramework;

namespace IslandGateway.CoreServicesBorrowed
{
    /// <summary>
    /// Wrapper around <see cref="Random"/> that facilitates deterministic unit testing.
    /// </summary>
    public class RandomWrapper : IRandom
    {
        private readonly Random random;

        /// <summary>
        /// Initializes a new instance of the <see cref="RandomWrapper"/> class.
        /// </summary>
        public RandomWrapper(Random random)
        {
            Contracts.CheckValue(random, nameof(random));
            this.random = random;
        }

        /// <inheritdoc/>
        public int Next()
        {
            return this.random.Next();
        }

        /// <inheritdoc/>
        public int Next(int maxValue)
        {
            return this.random.Next(maxValue);
        }

        /// <inheritdoc/>
        public int Next(int minValue, int maxValue)
        {
            return this.random.Next(minValue, maxValue);
        }
    }
}
