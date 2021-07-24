// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace IslandGateway.Common
{
    /// <summary>
    /// Implementation of <see cref="IProcessExiter"/> that uses <see cref="Environment.Exit(int)"/> to cause the current process to exit.
    /// </summary>
    /// <remarks>
    /// This does not currently work due to ASP .NET Core / .NET Core issues:
    ///  - https://github.com/dotnet/runtime/issues/50397: Deadlock when calling System.Environment.Exit(int exitCode) during Startup.Configure(..)
    ///  - https://github.com/dotnet/runtime/issues/50527: Make improvements to signal handling on .NET applications
    ///
    /// Prefer <see cref="FailFastProcessExiter"/> until these are fixed.
    /// </remarks>
    [Obsolete("This doesn/t work with ASP .NET Core hosts, see https://github.com/dotnet/runtime/issues/50397. Prefer FailFastProcessExiter until the underlying issue is fixed.")]
    public class ProcessExiter : IProcessExiter
    {
        private readonly ILogger<ProcessExiter> logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessExiter"/> class.
        /// </summary>
        public ProcessExiter(ILogger<ProcessExiter> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public virtual Task Exit(int exitCode, string message)
        {
            try
            {
                this.logger.LogError($"Exiting process with exit code '{exitCode}': {message}");
            }
            catch
            {
                // Do nothing...
            }

            Environment.Exit(exitCode);
            throw new InvalidOperationException("Execution should never get here...");
        }
    }
}
