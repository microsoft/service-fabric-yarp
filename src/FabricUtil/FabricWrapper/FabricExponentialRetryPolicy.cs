// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Yarp.ServiceFabric.ServiceFabricIntegration
{
    /// <summary>
    /// Structure providing the defaults for SF retry policy.
    /// </summary>
    public struct FabricExponentialRetryPolicy
    {
        /// <summary>
        /// Default intance of the policy.
        /// </summary>
        public static readonly FabricExponentialRetryPolicy Default = new()
        {
            NumAttempts = 3,
            InitialBackoffMs = 1000,
            MaxBackoffMs = 15000,
        };

        /// <summary>
        /// Total number of attempts allowed in the retry policy.
        /// </summary>
        public uint NumAttempts { get; init; }

        /// <summary>
        /// Initial retry backoff.
        /// </summary>
        public uint InitialBackoffMs { get; init; }

        /// <summary>
        /// Maximum time allowed for a backoff.
        /// </summary>
        public uint MaxBackoffMs { get; init; }

        /// <summary>
        /// Helper function to determine whether retry may be allowed by policy.
        /// On positive evaluation, next backoff is populated appropriately.
        /// </summary>
        /// <param name="attempt">Current attempt. Must be >= 1.</param>
        /// <param name="backoffBeforeNextAttempt">The next backoff.</param>
        /// <returns>Boolean whether retry is allowed by policy.</returns>
        public bool IsRetryAllowed(int attempt, out int backoffBeforeNextAttempt)
        {
            backoffBeforeNextAttempt = default;
            if (attempt <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attempt));
            }
            if (attempt < this.NumAttempts)
            {
                backoffBeforeNextAttempt = (int)Math.Min((Math.Pow(2, attempt) - 1) * this.InitialBackoffMs, this.MaxBackoffMs);
                return true;
            }
            return false;
        }
    }
}
