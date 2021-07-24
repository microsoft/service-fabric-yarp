﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common.Abstractions.Time;

namespace IslandGateway.Common.Util
{
    /// <summary>
    /// Provides a way to measure time in a monotonic fashion, immune to any system clock changes.
    /// The time is measured from the moment the class is instantiated.
    /// </summary>
    public sealed class MonotonicTimer : IMonotonicTimer
    {
        /// <summary>
        /// Specifies the minimum granularity of a scheduling tick. Larger values produce less precise scheduling. Smaller values
        /// produce unnecessary scheduling events, wasting CPU cycles and/or power.
        /// </summary>
        private static readonly TimeSpan MinimalInterval = TimeSpan.FromMilliseconds(0.1);

        /// <summary>
        /// Use a System.Diagnostics.StopWatch to measure time. Even though it has a poorer resolution, it serves this purpose very well.
        /// </summary>
        private readonly Stopwatch timeProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="MonotonicTimer"/> class.
        /// </summary>
        public MonotonicTimer()
        {
            this.timeProvider = Stopwatch.StartNew();
        }

        /// <inheritdoc />
        public TimeSpan CurrentTime => this.timeProvider.Elapsed;

        /// <inheritdoc />
        public async Task DelayUntil(TimeSpan expiryTime, CancellationToken cancellationToken)
        {
            // Note: this implementation could be improved by coalescing related expirations. For example, if there's a When(12:00 noon) and When(12:30pm), then
            // the second When doesn't need to start allocating Task.Delay timers until after the first expires.
            for (; ;)
            {
                var now = this.CurrentTime;
                if (now >= expiryTime)
                {
                    return;
                }

                var delay = TimeSpanUtil.Max(expiryTime - now, MinimalInterval);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
