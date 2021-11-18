// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;

namespace Yarp.ServiceFabric.Common
{
    /// <summary>
    /// Provides a method meant to exit the process when a non-recoverable failure happens.
    /// </summary>
    public interface IProcessExiter
    {
        /// <summary>
        /// Exits the process with the provided <paramref name="exitCode"/>.
        /// </summary>
        Task Exit(int exitCode, string message);
    }
}