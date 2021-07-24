// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common.Util;

namespace IslandGateway.CoreServicesBorrowed
{
    /// <summary>
    /// Provides a way to measure time that tracks the wall clock time. Unlike <see cref="MonotonicTimer"/>, this class allows the possibility
    /// of time rolling backwards (e.g. clock drift corrections), so should only be used for coarse-grained time.
    /// </summary>
    /// <remarks>
    /// We should remove this infavor of Core Framework's implementation. We cannot do this easily, however,
    /// because IslandGateway.Core does not reference CoreFramework and hence needs a different declaration of IWallClockTimer.
    /// </remarks>
    public sealed class WallClockTimer : IWallClockTimer
    {
        /// <summary>
        /// Specifies the minimum granularity of a scheduling tick. Larger values produce less precise scheduling. Smaller values
        /// produce unnecessary scheduling events, wasting CPU cycles and/or power.
        /// </summary>
        private static readonly TimeSpan MinimalInterval = TimeSpan.FromMilliseconds(10);

        /// <summary>
        /// Gets the current time.
        /// </summary>
        public DateTime CurrentTime => DateTime.UtcNow;

        /// <inheritdoc />
        public async Task When(DateTime until, CancellationToken cancellationToken)
        {
            // Note: this implementation could be improved by coalescing related expirations. For example, if there's a When(12:00 noon) and When(12:30pm), then
            // the second When doesn't need to start allocating Task.Delay timers until after the first expires.
            for (; ;)
            {
                var now = this.CurrentTime;
                if (now >= until)
                {
                    return;
                }

                var delay = TimeSpanUtil.Max(until - now, MinimalInterval);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
