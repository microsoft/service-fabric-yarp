// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace IslandGateway.Common.Util
{
    /// <summary>
    /// Time and utilities.
    /// </summary>
    public static class TimeSpanUtil
    {
        /// <summary>
        /// <see cref="TimeSpan"/> analog of <see cref="Math.Min(int, int)"/>; returns the smaller of two values.
        /// </summary>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        /// <returns>The smaller value of <paramref name="a"/> and <paramref name="b"/>.</returns>
        public static TimeSpan Min(TimeSpan a, TimeSpan b)
        {
            return TimeSpan.FromTicks(Math.Min(a.Ticks, b.Ticks));
        }

        /// <summary>
        /// <see cref="TimeSpan"/> analog of <see cref="Math.Max(int, int)"/>; returns the larger of two values.
        /// </summary>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        /// <returns>The larger value of <paramref name="a"/> and <paramref name="b"/>.</returns>
        public static TimeSpan Max(TimeSpan a, TimeSpan b)
        {
            return TimeSpan.FromTicks(Math.Max(a.Ticks, b.Ticks));
        }

        /// <summary>
        /// Clamps the given <paramref name="value"/> to be between <paramref name="min"/>
        /// and <paramref name="max"/>, inclusive.
        /// </summary>
        /// <param name="value">Value to clamp.</param>
        /// <param name="min">Minimum value.</param>
        /// <param name="max">Maximum value.</param>
        public static TimeSpan Clamp(this TimeSpan value, TimeSpan min, TimeSpan max)
        {
            if (max.Ticks < min.Ticks)
            {
                throw new ArgumentOutOfRangeException(nameof(max), $"{nameof(max)} must be larger than or equal to {nameof(min)}");
            }

            return TimeSpan.FromTicks(Math.Max(min.Ticks, Math.Min(max.Ticks, value.Ticks)));
        }
    }
}
