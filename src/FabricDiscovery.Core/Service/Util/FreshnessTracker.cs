// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;

namespace Yarp.ServiceFabric.FabricDiscovery.Util
{
    internal class FreshnessTracker
    {
        private readonly Stopwatch stopwatch = new Stopwatch();

        public TimeSpan Freshness
        {
            get
            {
                lock (this.stopwatch)
                {
                    return this.stopwatch.Elapsed;
                }
            }
        }

        public void SetFresh()
        {
            lock (this.stopwatch)
            {
                this.stopwatch.Restart();
            }
        }

        public void Reset()
        {
            lock (this.stopwatch)
            {
                this.stopwatch.Reset();
            }
        }
    }
}
