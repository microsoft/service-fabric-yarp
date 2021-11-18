// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Yarp.ServiceFabric.CoreServicesBorrowed.CoreFramework;

namespace Yarp.ServiceFabric.CoreServicesBorrowed.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="ILogger"/>
    /// to help with logging unhandled exceptions.
    /// </summary>
    public static class LoggerExtensions
    {
        /// <summary>
        /// Analogous of Core Framework's <c>LoggerFactoryExtensions.LogUnhandledExceptions</c>
        /// but which works on an <see cref="ILogger"/> instance.
        /// </summary>
        /// <remarks>
        /// This really belongs in Core Framework.
        /// </remarks>
        public static void LogUnhandledExceptions(this ILogger logger, Type callerType)
        {
            Contracts.CheckValue(logger, nameof(logger));
            Contracts.CheckValue(callerType, nameof(callerType));

            string assemblyName = callerType.Assembly.GetName().FullName;

            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) => logger.LogError($"UnhandledException in {assemblyName}: {eventArgs.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (sender, eventArgs) => logger.LogError($"UnobservedTaskException in {assemblyName}: {eventArgs.Exception}");
        }
    }
}
