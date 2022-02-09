// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Yarp.ServiceFabric.Common.Util;
using YarpProxy.Service.Lifecycle;

namespace Yarp.ServiceFabric.Service
{
    /// <summary>
    /// Delays shutdown of the ASP .NET Core Kestrel listener by a few seconds
    /// to account for the time it takes for Azure Load Balancer to notice we are shutting down.
    /// </summary>
    /// <remarks>
    /// Kestrel handles graceful server shutdown and request draining for all supported protocols.
    /// For HTTP/1.1 connections, Kestrel disables keep-alive and sets response header <c>Connection: close</c>
    /// on existing connections while draining traffic.
    /// For HTTP/2 connections, Kestrel sends a GOAWAY frame and continues to process existing streams.
    ///
    /// This means we don't have to worry about the low-level details of gracefully tearing down
    /// reused or long-running connections, and instead all we need to do is sequence events correctly.
    ///
    /// We implement the following sequence of steps:
    ///    1. Service Fabric asks us to shutdown gracefully by invoking <see cref="CloseAsync(CancellationToken)"/>
    ///    2. We mark that we are shutting down in the provided <see cref="shutdownStateManager"/>.
    ///       This causes SFYarp's health endpoint to respond an unhealthy status code for subsequent health probes
    ///    3. We wait some time to ensure Load Balancer has taken us out of rotation,
    ///       while we continue to accept and process new requests as normal
    ///    4. We initiate ASP .NET Core's graceful shutdown by forwarding the <see cref="CloseAsync(CancellationToken)"/> call
    ///       to the wrapped listener instance
    ///    5. ASP .NET Core gracefully drains all requests, and forcefully aborts after the shutdown timeout defined with
    ///       <see cref="HostingAbstractionsWebHostBuilderExtensions.UseShutdownTimeout(IWebHostBuilder, TimeSpan)"/>.
    ///    6. <see cref="CloseAsync(CancellationToken)"/> completes asynchronously, and the process exits soon after.
    /// </remarks>
    internal class DeferredCloseCommunicationListener : ICommunicationListener
    {
        private readonly TimeSpan delay;
        private readonly ShutdownStateManager shutdownStateManager;
        private readonly ICommunicationListener wrapped;
        private readonly ILogger logger;

        public DeferredCloseCommunicationListener(ICommunicationListener wrapped, TimeSpan delay, ShutdownStateManager shutdownStateManager, ILogger logger)
        {
            if (delay.Ticks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            this.wrapped = wrapped ?? throw new ArgumentNullException(nameof(wrapped));
            this.delay = delay;
            this.shutdownStateManager = shutdownStateManager ?? throw new ArgumentNullException(nameof(shutdownStateManager));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public Task<string> OpenAsync(CancellationToken cancellationToken)
        {
            this.logger.LogInformation($"[{nameof(DeferredCloseCommunicationListener)}] OpenAsync called.");
            return this.wrapped.OpenAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            this.logger.LogInformation($"[{nameof(DeferredCloseCommunicationListener)}] CloseAsync called, initiating shutdown and deferring teardown by {this.delay.TotalSeconds} seconds.");
            this.shutdownStateManager.MarkShuttingDown();

            using var registration = cancellationToken.Register(() => this.logger.LogWarning($"[{nameof(DeferredCloseCommunicationListener)}] Graceful shutdown preempted by external cancellation."));
            try
            {
                await Task.Delay(this.delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // If we run out of time before even the deferral is complete,
                // we still need to give a chance for the wrapped listener to close,
                // so swallow and proceed to forward the call to the wrapped instance.
                // even though we will call it with a token that is already canceled,
                // it may still need to do some work.
            }

            this.logger.LogInformation($"[{nameof(DeferredCloseCommunicationListener)}] Deferral elapsed, gracefully tearing down Kestrel.");
            var sw = ValueStopwatch.StartNew();
            string outcome = "error";
            try
            {
                await this.wrapped.CloseAsync(cancellationToken);
                outcome = "success";
            }
            finally
            {
                this.logger.LogInformation($"[{nameof(DeferredCloseCommunicationListener)}] Kestrel terminated with outcome '{outcome}' in {sw.Elapsed.TotalMilliseconds:F1} ms.");
            }
        }

        /// <inheritdoc/>
        public void Abort()
        {
            this.logger.LogInformation($"[{nameof(DeferredCloseCommunicationListener)}] Abort called.");
            this.wrapped.Abort();
        }
    }
}
