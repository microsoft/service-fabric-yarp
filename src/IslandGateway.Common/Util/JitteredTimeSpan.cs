// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Yarp.ServiceFabric.Common
{
    /// <summary>
    /// A <see cref="TimeSpan"/> with support for jitter.
    /// </summary>
    /// <remarks>
    /// Jittering time intervals is typically used to avoid multiple instances of a delayed action with the same start time and delay
    /// period occurring at the same future time. This commonly occurs when dealing with recurring behavior on node start, such as cache
    /// refresh or recycle, that would otherwise be synchronized due to nodes starting at the same time after deployment.
    /// </remarks>
    public readonly struct JitteredTimeSpan
    {
        private static readonly Random Random = new Random();

        private readonly TimeSpan baseValue;
        private readonly TimeSpan jitter;

        /// <summary>
        /// Initializes a new instance of the <see cref="JitteredTimeSpan"/> struct.
        /// </summary>
        /// <param name="baseValue">The base time span to which to apply the jitter.</param>
        /// <param name="jitter">The maximum jitter time span to add to the base time span when sampling.</param>
        public JitteredTimeSpan(TimeSpan baseValue, TimeSpan jitter)
        {
            if (baseValue < TimeSpan.Zero)
            {
                throw new ArgumentException("Value must be positive", nameof(baseValue));
            }

            if (jitter < TimeSpan.Zero)
            {
                throw new ArgumentException("Value must be positive", nameof(jitter));
            }

            try
            {
                _ = baseValue + jitter;
            }
            catch (OverflowException)
            {
                throw new ArgumentException("Maximum value must not overflow", nameof(jitter));
            }

            this.baseValue = baseValue;
            this.jitter = jitter;
        }

        /// <summary>
        /// Gets the minimum possible sample value.
        /// </summary>
        public TimeSpan Min => this.baseValue;

        /// <summary>
        /// Gets the maximum possible sample value.
        /// </summary>
        public TimeSpan Max => this.baseValue + this.jitter;

        /// <summary>
        /// Allows explicit conversion of <see cref="TimeSpan"/> to a <see cref="JitteredTimeSpan"/> with zero jitter.
        /// </summary>
        public static explicit operator JitteredTimeSpan(TimeSpan timeSpan) => new JitteredTimeSpan(timeSpan, TimeSpan.Zero);

        /// <summary>
        /// Samples the jittered time span, returning a <see cref="TimeSpan"/> value within the configured range.
        /// </summary>
        public TimeSpan Sample()
        {
            return this.baseValue + TimeSpan.FromTicks((long)(Random.NextDouble() * this.jitter.Ticks));
        }
    }
}
