// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Yarp.ServiceFabric.Common
{
    /// <summary>
    /// Base class for an <see cref="IHostedService"/> that is mission-critical
    /// and which should cause the entire process to exit in case of failure.
    /// </summary>
    public abstract class CriticalBackgroundService : BackgroundService
    {
        private const int TooManyFailuresExitCode = 0x0badfab0;
        private const int TimeoutExitCode = 0x0badfab1;

        private readonly WorkerOptions options;
        private readonly IProcessExiter processExiter;
        private readonly ILogger logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CriticalBackgroundService"/> class.
        /// </summary>
        protected CriticalBackgroundService(
            WorkerOptions options,
            IProcessExiter processExiter,
            ILogger logger)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.processExiter = processExiter ?? throw new ArgumentNullException(nameof(processExiter));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public override async Task StartAsync(CancellationToken cancellation)
        {
            if (this.options.Disabled)
            {
                this.logger.LogInformation($"{this.GetType().FullName}.{nameof(this.StartAsync)}, no-op because {nameof(this.options.Disabled)}=true");
                return;
            }

            this.logger.LogInformation($"{this.GetType().FullName}.{nameof(this.StartAsync)}");
            await this.AbortAfterTimeoutAsync(this.InitAsync, cancellation);
            await base.StartAsync(cancellation);
        }

        /// <inheritdoc/>
        public override async Task StopAsync(CancellationToken cancellation)
        {
            if (this.options.Disabled)
            {
                this.logger.LogInformation($"{this.GetType().FullName}.{nameof(this.StopAsync)}, no-op because {nameof(this.options.Disabled)}=true");
                return;
            }

            this.logger.LogInformation($"{this.GetType().FullName}.{nameof(this.StopAsync)}");

            await Task.WhenAll(WrapTeardownAsync(cancellation), base.StopAsync(cancellation));

            async Task WrapTeardownAsync(CancellationToken cancellation)
            {
                // If teardown is taking too long, forget about the task.
                // This is to protect us from the possibility of graceful teardown getting stuck preventing the service from shutting down
                if (!await AsyncUtil.IgnoreOnCancellation(this.TeardownAsync(cancellation), cancellation))
                {
                    this.logger.LogWarning($"{this.GetType().FullName}.TeardownAsync took too long and will now be ignored.");
                }
            }
        }

        /// <inheritdoc/>
        protected sealed override async Task ExecuteAsync(CancellationToken cancellation)
        {
            this.logger.LogInformation($"{this.GetType().FullName}.{nameof(this.ExecuteAsync)} started");

            try
            {
                await this.RunAsync(cancellation);
            }
            catch (Exception ex)
            {
                string message = $"Aborting {this.GetType().FullName} with exit code 0x{TooManyFailuresExitCode:x} after failure.";
                this.logger.LogError(ex, message);
                await this.processExiter.Exit(TooManyFailuresExitCode, message);
                throw new InvalidOperationException("Execution never gets here");
            }
        }

        /// <summary>
        /// Called once when starting. This can be used to initialize any state that is critical **before** the .NET Host is considered ready.
        /// If the implementation throws, it will bring down the process.
        /// </summary>
        protected virtual Task InitAsync(CancellationToken cancellation)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called once during graceful termination of the service.
        /// The implementation should complete quickly, and if it doesn't,
        /// the resulting task is ignored after <paramref name="cancellation"/> is signaled.
        /// This prevents the Host shutdown from blocking indefinitely in case there are problems during teardown.
        /// </summary>
        protected virtual Task TeardownAsync(CancellationToken cancellation)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Executes one iteration of the background service.
        /// This method is called repeatedly for as long as the service is alive and running.
        /// Note that if delays between executions are desired, they must be implemented manually on this method.
        /// </summary>
        protected abstract Task RunAsync(CancellationToken cancellation);

        private async Task AbortAfterTimeoutAsync(Func<CancellationToken, Task> func, CancellationToken stoppingToken)
        {
            if (!await AsyncUtil.ExecuteWithTimeout(func, this.options.InitializationTimeout, stoppingToken))
            {
                string message = $"Aborting {this.GetType().FullName} with exit code 0x{TimeoutExitCode:x} after reaching abort timeout ({this.options.InitializationTimeout}).";
                this.logger.LogError(message);
                await this.processExiter.Exit(TimeoutExitCode, message);
                throw new InvalidOperationException("Execution never gets here");
            }
        }

        /// <summary>
        /// A set of options used to configure behavior of <see cref="CriticalBackgroundService"/>.
        /// </summary>
        protected class WorkerOptions
        {
            /// <summary>
            /// Setting this disabled option will cause all background service operations to no-op.
            /// </summary>
            public bool Disabled { get; set; }

            /// <summary>
            /// Aborts (exits the process) if <see cref="InitAsync"/> takes longer than this amount.
            /// </summary>
            public TimeSpan? InitializationTimeout { get; set; }
        }
    }
}
