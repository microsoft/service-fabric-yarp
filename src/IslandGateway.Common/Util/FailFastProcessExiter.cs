// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Yarp.ServiceFabric.Common
{
    /// <inheritdoc/>
    public class FailFastProcessExiter : IProcessExiter
    {
        private readonly ILogger<FailFastProcessExiter> logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="FailFastProcessExiter"/> class.
        /// </summary>
        public FailFastProcessExiter(ILogger<FailFastProcessExiter> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public virtual Task Exit(int exitCode, string message)
        {
            try
            {
                this.logger.LogError($"Exiting (FailFast) process with exit code '{exitCode}': {message}");
            }
            catch
            {
                // Do nothing...
            }

            // BUG: https://github.com/dotnet/runtime/issues/50397 - Deadlock when calling System.Environment.Exit(int exitCode) during Startup.Configure(..)
            // FailFast seems to work reliably when Environment.Exit doesn't....
            Environment.FailFast(message);
            throw new InvalidOperationException("Execution should never get here...");
        }
    }
}
