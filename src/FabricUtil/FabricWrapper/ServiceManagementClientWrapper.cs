// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Fabric;
using System.Fabric.Description;
using System.Threading;
using System.Threading.Tasks;

namespace Yarp.ServiceFabric.ServiceFabricIntegration
{
    /// <summary>
    /// A wrapper class for the service fabric client SDK.
    /// See Microsoft documentation: https://docs.microsoft.com/en-us/dotnet/api/system.fabric.fabricclient.servicemanagementclient?view=azure-dotnet .
    /// </summary>
    public class ServiceManagementClientWrapper : IServiceManagementClientWrapper
    {
        private readonly Lazy<FabricClient.ServiceManagementClient> lazyServiceManagementClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceManagementClientWrapper"/> class.
        /// Wraps QueryManager, PropertyManager and ServiceManager SF SDK.
        /// </summary>
        public ServiceManagementClientWrapper()
        {
            // Temporary work around: unit tests were creating FabricClient, creating unnecessary dependency on service fabric runtime. For
            // now, make this lazy init to remove the dependency on service fabric runtime during unit test execution.
            this.lazyServiceManagementClient = new Lazy<FabricClient.ServiceManagementClient>(() => new FabricClient().ServiceManager);
        }

        // Represents the enabling of the services to be managed.
        private FabricClient.ServiceManagementClient ServiceManagementClient => this.lazyServiceManagementClient.Value;

        /// <summary>
        /// Gets the provisioned service manifest document in the specified application type name and application type version.
        /// Also takes in timeout interval, which is the maximum of time the system will allow this operation to continue before returning.
        /// </summary>
        public async Task<string> GetServiceManifestAsync(string applicationTypeName, string applicationTypeVersion, string serviceManifestName, TimeSpan timeout, CancellationToken cancellationToken)
        {
            return await ExceptionsHelper.TranslateCancellations(
                () => this.ServiceManagementClient.GetServiceManifestAsync(applicationTypeName, applicationTypeVersion, serviceManifestName, timeout, cancellationToken),
                cancellationToken);
        }

        /// <summary>
        /// Subscribes to service endpoint change notifications.
        /// </summary>
        /// <returns>Asynchronously returns a function that can be called to unsubscribe.</returns>
        public async Task<Func<CancellationToken, Task>> SubscribeToNotifications(Action<ServiceNotificationWrapper> func, TimeSpan apiTimeout, CancellationToken cancellation)
        {
            // See: https://msazure.visualstudio.com/One/_git/Hydra-RP?path=%2Fsrc%2FDnsHelper%2FArnoEdgeProxy%2FEdgeProxyService%2FRouteDiscoveryClient.cs&_a=contents&version=GBmaster
            this.ServiceManagementClient.ServiceNotificationFilterMatched += Handler;

            try
            {
                var filterId = await FabricCallHelper.RunWithExponentialRetries(
                    (attempt, cancellation) => this.ServiceManagementClient.RegisterServiceNotificationFilterAsync(
                        new ServiceNotificationFilterDescription(
                            name: new Uri("fabric:"),
                            matchNamePrefix: true,
                            matchPrimaryChangeOnly: false),
                        apiTimeout,
                        cancellation),
                    FabricExponentialRetryPolicy.Default,
                    cancellation);

                return async unsubscribeCancellation =>
                {
                    try
                    {
                        await FabricCallHelper.RunWithExponentialRetries(
                            async (attempt, unsubscribeCancellation) =>
                            {
                                await this.ServiceManagementClient.UnregisterServiceNotificationFilterAsync(filterId, apiTimeout, unsubscribeCancellation);
                                return 0;
                            },
                            FabricExponentialRetryPolicy.Default,
                            unsubscribeCancellation);
                    }
                    finally
                    {
                        this.ServiceManagementClient.ServiceNotificationFilterMatched -= Handler;
                    }
                };
            }
            catch (Exception)
            {
                this.ServiceManagementClient.ServiceNotificationFilterMatched -= Handler;
                throw;
            }

            void Handler(object sender, EventArgs args)
            {
                if (args is FabricClient.ServiceManagementClient.ServiceNotificationEventArgs notificationArgs)
                {
                    var notification = notificationArgs.Notification;
                    func(new ServiceNotificationWrapper
                    {
                        ServiceName = new ServiceNameKey(notification.ServiceName),
                        PartitionId = notification.PartitionId,
                        Endpoints = notification.Endpoints,
                        PartitionInfo = notification.PartitionInfo,
                    });
                }
            }
        }
    }
}
