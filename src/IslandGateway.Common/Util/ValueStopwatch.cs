﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;

namespace IslandGateway.Common.Util
{
    /// <summary>
    /// Value-type replacement for <see cref="Stopwatch"/> which avoids allocations.
    /// </summary>
    /// <remarks>
    /// Inspired on <seealso href="https://github.com/dotnet/extensions/blob/master/src/Shared/src/ValueStopwatch/ValueStopwatch.cs"/>.
    /// </remarks>
    public struct ValueStopwatch
    {
        private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

        private long startTimestamp;

        private ValueStopwatch(long startTimestamp)
        {
            this.startTimestamp = startTimestamp;
        }

        /// <summary>
        /// Gets the time elapsed since the stopwatch was created with <see cref="StartNew"/>.
        /// </summary>
        public TimeSpan Elapsed
        {
            get
            {
                // Start timestamp can't be zero in an initialized ValueStopwatch. It would have to be literally the first thing executed when the machine boots to be 0.
                // So it being 0 is a clear indication of default(ValueStopwatch)
                if (this.startTimestamp == 0)
                {
                    throw new InvalidOperationException("An uninitialized, or 'default', ValueStopwatch cannot be used to get elapsed time.");
                }

                var end = Stopwatch.GetTimestamp();
                var timestampDelta = end - this.startTimestamp;
                var ticks = (long)(TimestampToTicks * timestampDelta);
                return new TimeSpan(ticks);
            }
        }

        /// <summary>
        /// Creates a new <see cref="ValueStopwatch"/> that is ready to be used.
        /// </summary>
        public static ValueStopwatch StartNew() => new ValueStopwatch(Stopwatch.GetTimestamp());
    }
}
