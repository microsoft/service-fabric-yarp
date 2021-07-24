// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace YarpProxy.Service.Lifecycle
{
    /// <summary>
    /// Helps manage graceful teardown of Island Gateway.
    /// </summary>
    public class ShutdownStateManager
    {
        private volatile bool isShuttingDown;

        /// <summary>
        /// Gets a value indcating whether Island gateway is currently shutting down.
        /// </summary>
        public bool IsShuttingDown => this.isShuttingDown;

        /// <summary>
        /// Marks that Island Gateway graceful shutdown has commenced.
        /// </summary>
        public void MarkShuttingDown()
        {
            this.isShuttingDown = true;
        }
    }
}
