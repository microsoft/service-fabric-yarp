// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace IslandGateway.ServiceFabricIntegration
{
    /// <summary>
    /// A wrapper for the service fabric service management client SDK to make service fabric API unit testable.
    /// See Microsoft documentation: https://docs.microsoft.com/en-us/dotnet/api/system.fabric.fabricclient.servicemanagementclient?view=azure-dotnet .
    /// </summary>
    public interface IServiceManagementClientWrapper
    {
        /// <summary>
        /// Gets the provisioned service manifest document in the specified application type name and application type version.
        /// Also takes in timeout interval, which is the maximum of time the system will allow this operation to continue before returning.
        /// </summary>
        Task<string> GetServiceManifestAsync(string applicationTypeName, string applicationTypeVersion, string serviceManifestName, TimeSpan timeout, CancellationToken cancellationToken);

        /// <summary>
        /// Subscribes to Service Fabric endpoint change notifications.
        /// </summary>
        /// <returns>
        /// A task that completes with a <see cref="Func{T, TResult}"/> which can be invoked asynchronously to unsubscribe.
        /// The returned func accepts a cancellation token to optionally cancel the unsubscribe operation.
        /// </returns>
        Task<Func<CancellationToken, Task>> SubscribeToNotifications(Action<ServiceNotificationWrapper> func, TimeSpan apiTimeout, CancellationToken cancellation);
    }
}
