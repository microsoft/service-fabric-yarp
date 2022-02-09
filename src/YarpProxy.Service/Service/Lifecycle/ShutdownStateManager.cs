// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace YarpProxy.Service.Lifecycle
{
    /// <summary>
    /// Helps manage graceful teardown of SFYarp.
    /// </summary>
    public class ShutdownStateManager
    {
        private volatile bool isShuttingDown;

        /// <summary>
        /// Gets a value indcating whether SFYarp is currently shutting down.
        /// </summary>
        public bool IsShuttingDown => this.isShuttingDown;

        /// <summary>
        /// Marks that SFYarp graceful shutdown has commenced.
        /// </summary>
        public void MarkShuttingDown()
        {
            this.isShuttingDown = true;
        }
    }
}
