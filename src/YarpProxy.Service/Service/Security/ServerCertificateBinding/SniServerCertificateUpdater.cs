// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Abstractions.Time;
using Yarp.ServiceFabric.Common.Util;
using Yarp.ServiceFabric.Core.Abstractions;
using Yarp.ServiceFabric.CoreServicesBorrowed;
using Yarp.ServiceFabric.CoreServicesBorrowed.CoreFramework;

namespace Yarp.ServiceFabric.Core.Service.Security.ServerCertificateBinding
{
    /// <summary>
    /// Default implementation of <see cref="ISniServerCertificateSelector"/>.
    /// </summary>
    internal sealed class SniServerCertificateUpdater : IHostedService
    {
        /// <summary>
        /// How often to rescan certificates, expressed as the time interval between scans.
        /// </summary>
        // TODO: Make this configurable.
        private static readonly TimeSpan DefaultRescanInterval = TimeSpan.FromMinutes(5);

        private readonly ILogger<SniServerCertificateUpdater> logger;
        private readonly IOperationLogger operationLogger;
        private readonly IMonotonicTimer timer;
        private readonly ISniServerCertificateSelector selector;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly SniServerCertificateSelectorOptions options = new SniServerCertificateSelectorOptions();

        private Task task;

        public SniServerCertificateUpdater(ILogger<SniServerCertificateUpdater> logger, IOperationLogger operationLogger, IMonotonicTimer timer, ISniServerCertificateSelector selector)
        {
            Contracts.CheckValue(logger, nameof(logger));
            Contracts.CheckValue(operationLogger, nameof(operationLogger));
            Contracts.CheckValue(timer, nameof(timer));
            Contracts.CheckValue(selector, nameof(selector));
            this.logger = logger;
            this.operationLogger = operationLogger;
            this.timer = timer;
            this.selector = selector;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Sanity check. We don't expect two calls.
            Contracts.Check(this.task == null, "Error, StartAsync already called");

            await this.operationLogger.ExecuteAsync(
                $"{nameof(SniServerCertificateUpdater)}.{nameof(this.StartAsync)}",
                async () =>
                {
                    await this.operationLogger.ExecuteAsync(
                        $"{nameof(SniServerCertificateUpdater)} initial update",
                        () => this.selector.UpdateAsync(this.options));
                    this.task = TaskScheduler.Current.Run(this.ExecuteWrapperAsync);
                });
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.operationLogger.ExecuteAsync(
                $"{nameof(SniServerCertificateUpdater)}.{nameof(this.StopAsync)}",
                async () =>
                {
                    var task = this.task;
                    if (task != null)
                    {
                        this.cts.Cancel();
                        this.task = null;
                        await task;
                    }
                });
        }

        private async Task ExecuteWrapperAsync()
        {
            try
            {
                await this.ExecuteAsync();
            }
            catch (Exception ex)
            {
                // Unhandled exception. Log it and swallow, otherwise this would raise UnobservedTaskException
                this.logger.LogError(ex, $"Unhandled exception in {nameof(SniServerCertificateSelector)}.{nameof(this.ExecuteAsync)}");
            }
        }

        private async Task ExecuteAsync()
        {
            while (true)
            {
                try
                {
                    await this.timer.Delay(DefaultRescanInterval, this.cts.Token);
                    await this.operationLogger.ExecuteAsync(
                            $"{nameof(SniServerCertificateUpdater)} iteration",
                            () => this.selector.UpdateAsync(this.options));
                }
                catch (OperationCanceledException) when (this.cts.Token.IsCancellationRequested)
                {
                    // Graceful shutdown...
                    break;
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    // Swallow non-fatal exceptions. The exception was already logged by `operationLogger`.
                }
            }
        }
    }
}
