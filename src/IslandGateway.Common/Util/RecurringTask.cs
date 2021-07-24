// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace IslandGateway.Common
{
    /// <summary>
    /// Allows fluent configuration for tasks that need to recur repeatedly.
    /// </summary>
    public class RecurringTask
    {
        private readonly Func<CancellationToken, Task> action;
        private ILogger logger;
        private string activityName;
        private IEnumerable<KeyValuePair<string, string>> customProperties;
        private JitteredTimeSpan? iterationDelay;
        private TimeSpan? iterationTimeout;
        private Func<Exception, int, bool> abortCondition;
        private int maxConcurrency = 1;
        private int currentConcurrency;
        private IOperationLogger operationLogger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecurringTask"/> class.
        /// </summary>
        public RecurringTask(Func<CancellationToken, Task> action)
        {
            this.action = action ?? throw new ArgumentNullException(nameof(action));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RecurringTask"/> class.
        /// </summary>
        public RecurringTask(Action action)
            : this(_ =>
            {
                action();
                return Task.CompletedTask;
            })
        {
        }

        /// <summary>
        /// Enables per-iteration logging on the task with the specified parameters.
        /// </summary>
        public RecurringTask WithLogging(ILogger logger, IOperationLogger operationLogger, string activityName)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
            this.activityName = activityName ?? throw new ArgumentNullException(nameof(activityName));
            return this;
        }

        /// <summary>
        /// Sets the custom properties to set when logging iteration execution.
        /// </summary>
        public RecurringTask WithCustomLoggingProperties(IEnumerable<KeyValuePair<string, string>> customProperties)
        {
            this.customProperties = customProperties;
            return this;
        }

        /// <summary>
        /// Configures the delay between iteration executions.
        /// </summary>
        public RecurringTask WithIterationDelay(JitteredTimeSpan? delay)
        {
            this.iterationDelay = delay;
            return this;
        }

        /// <summary>
        /// Configures the timeout on iteration action.
        /// </summary>
        public RecurringTask WithIterationTimeout(TimeSpan? timeout)
        {
            this.iterationTimeout = timeout;
            return this;
        }

        /// <summary>
        /// Configures the task to abort after any iteration failures.
        /// </summary>
        public RecurringTask WithAbortOnFailure()
        {
            this.abortCondition = (_, _) => true;
            return this;
        }

        /// <summary>
        /// Configures the task to abort after the specified number of consecutive iteration failures.
        /// </summary>
        public RecurringTask WithAbortOnConsecutiveFailures(int consecutiveFailures)
        {
            this.abortCondition = (_, consecutiveFailureCount) => consecutiveFailureCount >= consecutiveFailures;
            return this;
        }

        /// <summary>
        /// Configures the task to abort after the specified condition is met.
        /// </summary>
        public RecurringTask WithAbortCondition(Func<bool> condition)
        {
            this.abortCondition = (_, _) => condition();
            return this;
        }

        /// <summary>
        /// Configures the task to abort after the specified condition is met.
        /// </summary>
        public RecurringTask WithAbortCondition(Func<Exception, int, bool> condition)
        {
            this.abortCondition = condition;
            return this;
        }

        /// <summary>
        /// Runs the recurring task until cancelled or aborted.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellation)
        {
            this.logger?.LogInformation($"Recurring task {this.activityName} started");
            int consecutiveFailures = 0;

            while (true)
            {
                try
                {
                    if (this.iterationDelay.HasValue)
                    {
                        await Task.Delay(this.iterationDelay.Value.Sample(), cancellation);
                    }

                    await this.RunIterationAsync(cancellation);

                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    this.logger?.LogInformation($"Recurring task {this.activityName} ended gracefully");
                    return;
                }
                catch (Exception exception) when (this.LogAndFilterAbortFailures(exception, ++consecutiveFailures))
                {
                    // NOTE: The exception was already logged by the exception filter. If execution gets here, it is
                    // because we have decided to swallow the exception this time. We may decide to abort for a future
                    // execution, in which time we would allow the exception to bubble out, and a respecting caller such
                    // as CriticalBackgroundService would then proceed to terminate the current process, as is desired.
                }
            }
        }

        /// <summary>
        /// Runs a single iteration of the recurring task.
        /// </summary>
        public Task RunIterationAsync(CancellationToken cancellation)
        {
            try
            {
                if (Interlocked.Increment(ref this.currentConcurrency) > this.maxConcurrency)
                {
                    throw new InvalidOperationException($"Recurring task {this.activityName} has a maximum concurrency of {this.maxConcurrency} but a current concurrency of {this.currentConcurrency}.");
                }

                return this.operationLogger != null
                    ? this.operationLogger.ExecuteRootAsync(
                        this.activityName,
                        () =>
                        {
                            if (this.customProperties != null)
                            {
                                foreach (var prop in this.customProperties)
                                {
                                    this.operationLogger.Context.SetProperty(prop.Key, prop.Value);
                                }
                            }
                            return AsyncUtil.ExecuteWithTimeout(this.action, this.iterationTimeout, cancellation);
                        })
                    : AsyncUtil.ExecuteWithTimeout(this.action, this.iterationTimeout, cancellation);
            }
            finally
            {
                Interlocked.Decrement(ref this.currentConcurrency);
            }
        }

        private bool LogAndFilterAbortFailures(Exception exception, int consecutiveFailures)
        {
            this.logger?.LogError(exception, $"Recurring task {this.activityName} iteration failed. Consecutive failures: {consecutiveFailures}");

            if (this.abortCondition != null && this.abortCondition(exception, consecutiveFailures))
            {
                this.logger?.LogError($"Aborting recurring task {this.activityName} after {consecutiveFailures} consecutive failures.");
                return false;
            }

            return true;
        }
    }
}
