// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Fabric.Query;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.ServiceFabricIntegration;

namespace IslandGateway.FabricDiscovery.FabricWrapper
{
    /// <summary>
    /// Wraps Service Fabric client SDK QueryClient api's.
    /// <see href="https://docs.microsoft.com/en-us/dotnet/api/system.fabric.fabricclient.queryclient?view=azure-dotnet"/>.
    /// </summary>
    public class QueryClientWrapper : IQueryClientWrapper
    {
        private readonly IOperationLogger operationLogger;
        private readonly FabricClient.QueryClient queryClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="QueryClientWrapper"/> class.
        /// </summary>
        public QueryClientWrapper(IOperationLogger operationLogger)
        {
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
            this.queryClient = new FabricClient().QueryManager;
        }

        /// <inheritdoc/>
        public virtual async IAsyncEnumerable<ApplicationTypeWrapper> GetApplicationTypesAsync(PagedApplicationTypeQueryDescription query, TimeSpan eachApiCallTimeout, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = query ?? throw new ArgumentNullException(nameof(query));

            int pageIndex = 0;
            do
            {
                // TODO: Retry
                var page = await this.operationLogger.ExecuteAsync(
                    "FabricApi.GetApplicationTypePagedListAsync",
                    () => ExceptionsHelper.TranslateCancellations(
                        func: static state => state.queryClient.GetApplicationTypePagedListAsync(
                            queryDescription: state.query,
                            timeout: state.eachApiCallTimeout,
                            cancellationToken: state.cancellationToken),
                        state: (this.queryClient, query, eachApiCallTimeout, cancellationToken),
                        cancellation: cancellationToken),
                    new[]
                    {
                        KeyValuePair.Create(nameof(query.ApplicationTypeNameFilter), query.ApplicationTypeNameFilter ?? string.Empty),
                        KeyValuePair.Create(nameof(query.ApplicationTypeVersionFilter), query.ApplicationTypeVersionFilter ?? string.Empty),
                        KeyValuePair.Create("page", (pageIndex++).ToString()),
                    });

                foreach (var item in page)
                {
                    yield return new ApplicationTypeWrapper
                    {
                        ApplicationTypeName = new ApplicationTypeNameKey(item.ApplicationTypeName),
                        ApplicationTypeVersion = new ApplicationTypeVersionKey(item.ApplicationTypeVersion),
                        DefaultParameters = MapAppParameters(item.DefaultParameters),
                        Status = item.Status,
                    };
                }

                query.ContinuationToken = page.ContinuationToken;
            }
            while (!string.IsNullOrEmpty(query.ContinuationToken));
        }

        /// <inheritdoc/>
        public virtual async IAsyncEnumerable<ApplicationWrapper> GetApplicationsAsync(ApplicationQueryDescription query, TimeSpan eachApiCallTimeout, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = query ?? throw new ArgumentNullException(nameof(query));

            int pageIndex = 0;
            do
            {
                // TODO: Retry
                var page = await this.operationLogger.ExecuteAsync(
                    "FabricApi.GetApplicationPagedListAsync",
                    () => ExceptionsHelper.TranslateCancellations(
                        func: static state => state.queryClient.GetApplicationPagedListAsync(
                            applicationQueryDescription: state.query,
                            timeout: state.eachApiCallTimeout,
                            cancellationToken: state.cancellationToken),
                        state: (this.queryClient, query, eachApiCallTimeout, cancellationToken),
                        cancellation: cancellationToken),
                    new[]
                    {
                        KeyValuePair.Create(nameof(query.ApplicationTypeNameFilter), query.ApplicationTypeNameFilter ?? string.Empty),
                        KeyValuePair.Create(nameof(query.ApplicationNameFilter), query.ApplicationNameFilter != null ? query.ApplicationNameFilter.ToString() : string.Empty),
                        KeyValuePair.Create("page", (pageIndex++).ToString()),
                    });

                foreach (var item in page)
                {
                    yield return new ApplicationWrapper
                    {
                        ApplicationName = new ApplicationNameKey(item.ApplicationName),
                        ApplicationTypeName = new ApplicationTypeNameKey(item.ApplicationTypeName),
                        ApplicationTypeVersion = new ApplicationTypeVersionKey(item.ApplicationTypeVersion),
                        ApplicationParameters = MapAppParameters(item.ApplicationParameters),
                    };
                }

                query.ContinuationToken = page.ContinuationToken;
            }
            while (!string.IsNullOrEmpty(query.ContinuationToken));
        }

        /// <inheritdoc/>
        public virtual async Task<ApplicationNameKey> GetApplicationNameAsync(ServiceNameKey serviceName, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var result = await this.operationLogger.ExecuteAsync(
                "FabricApi.GetApplicationNameAsync",
                () => ExceptionsHelper.TranslateCancellations(
                    func: static state => state.queryClient.GetApplicationNameAsync(
                        serviceName: state.serviceName,
                        timeout: state.timeout,
                        cancellationToken: state.cancellationToken),
                    state: (this.queryClient, serviceName, timeout, cancellationToken),
                    cancellation: cancellationToken),
                new[]
                {
                    KeyValuePair.Create(nameof(serviceName), serviceName.ToString() ?? string.Empty),
                });
            return new ApplicationNameKey(result.ApplicationName);
        }

        /// <inheritdoc/>
        public virtual async IAsyncEnumerable<ServiceTypeWrapper> GetServiceTypesAsync(ApplicationTypeNameKey applicationTypeName, ApplicationTypeVersionKey applicationTypeVersion, TimeSpan eachApiCallTimeout, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(applicationTypeName))
            {
                throw new ArgumentException(nameof(applicationTypeName));
            }
            if (string.IsNullOrEmpty(applicationTypeVersion))
            {
                throw new ArgumentException(nameof(applicationTypeVersion));
            }

            // TODO: Retry
            var results = await this.operationLogger.ExecuteAsync(
                "FabricApi.GetServiceTypeListAsync",
                () => ExceptionsHelper.TranslateCancellations(
                    func: static state => state.queryClient.GetServiceTypeListAsync(
                        applicationTypeName: state.applicationTypeName,
                        applicationTypeVersion: state.applicationTypeVersion,
                        serviceTypeNameFilter: null,
                        timeout: state.eachApiCallTimeout,
                        cancellationToken: state.cancellationToken),
                    state: (this.queryClient, applicationTypeName, applicationTypeVersion, eachApiCallTimeout, cancellationToken),
                    cancellation: cancellationToken),
                new[]
                {
                    KeyValuePair.Create(nameof(applicationTypeName), applicationTypeName.ToString() ?? string.Empty),
                    KeyValuePair.Create(nameof(applicationTypeVersion), applicationTypeVersion.ToString() ?? string.Empty),
                });

            foreach (var item in results)
            {
                yield return new ServiceTypeWrapper
                {
                    ServiceManifestName = item.ServiceManifestName,
                    ServiceManifestVersion = item.ServiceManifestVersion,
                    ServiceTypeName = new ServiceTypeNameKey(item.ServiceTypeDescription.ServiceTypeName),
                    ServiceTypeKind = item.ServiceTypeDescription.ServiceTypeKind,
                    Extensions = item.ServiceTypeDescription.Extensions,
                };
            }
        }

        /// <inheritdoc/>
        public virtual async IAsyncEnumerable<ServiceWrapper> GetServicesAsync(ServiceQueryDescription query, TimeSpan eachApiCallTimeout, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = query ?? throw new ArgumentNullException(nameof(query));

            int pageIndex = 0;
            do
            {
                // TODO: Retry
                var page = await this.operationLogger.ExecuteAsync(
                    "FabricApi.GetServicePagedListAsync",
                    () => ExceptionsHelper.TranslateCancellations(
                        func: static state => state.queryClient.GetServicePagedListAsync(
                            serviceQueryDescription: state.query,
                            timeout: state.eachApiCallTimeout,
                            cancellationToken: state.cancellationToken),
                        state: (this.queryClient, query, eachApiCallTimeout, cancellationToken),
                        cancellation: cancellationToken),
                    new[]
                    {
                        KeyValuePair.Create(nameof(query.ServiceTypeNameFilter), query.ServiceTypeNameFilter ?? string.Empty),
                        KeyValuePair.Create(nameof(query.ServiceNameFilter), query.ServiceNameFilter != null ? query.ServiceNameFilter.ToString() : string.Empty),
                        KeyValuePair.Create("page", (pageIndex++).ToString()),
                    });

                foreach (var item in page)
                {
                    yield return new ServiceWrapper
                    {
                        ServiceName = new ServiceNameKey(item.ServiceName),
                        ServiceTypeName = new ServiceTypeNameKey(item.ServiceTypeName),
                        ServiceManifestVersion = item.ServiceManifestVersion,
                        ServiceKind = item.ServiceKind,
                        ServiceStatus = item.ServiceStatus,
                    };
                }

                query.ContinuationToken = page.ContinuationToken;
            }
            while (!string.IsNullOrEmpty(query.ContinuationToken));
        }

        /// <inheritdoc/>
        public virtual async IAsyncEnumerable<PartitionWrapper> GetPartitionsAsync(ServiceNameKey serviceName, TimeSpan eachApiCallTimeout, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = serviceName.Value ?? throw new ArgumentNullException(nameof(serviceName));

            int pageIndex = 0;
            string continuationToken = null;
            do
            {
                var page = await this.operationLogger.ExecuteAsync(
                    "FabricApi.GetPartitionListAsync",
                    () => ExceptionsHelper.TranslateCancellations(
                        func: static state => state.queryClient.GetPartitionListAsync(
                            serviceName: state.serviceName,
                            partitionIdFilter: null,
                            continuationToken: state.continuationToken,
                            timeout: state.eachApiCallTimeout,
                            cancellationToken: state.cancellationToken),
                        state: (this.queryClient, serviceName, continuationToken, eachApiCallTimeout, cancellationToken),
                        cancellation: cancellationToken),
                    new[]
                    {
                        KeyValuePair.Create(nameof(serviceName), serviceName.ToString() ?? string.Empty),
                        KeyValuePair.Create("page", (pageIndex++).ToString()),
                    });

                foreach (var item in page)
                {
                    yield return new PartitionWrapper
                    {
                        PartitionId = item.PartitionInformation.Id,
                    };
                }

                continuationToken = page.ContinuationToken;
            }
            while (!string.IsNullOrEmpty(continuationToken));
        }

        /// <inheritdoc/>
        public virtual async IAsyncEnumerable<ReplicaWrapper> GetReplicasAsync(Guid partitionId, TimeSpan eachApiCallTimeout, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int pageIndex = 0;
            string continuationToken = null;
            do
            {
                var page = await this.operationLogger.ExecuteAsync(
                    "FabricApi.GetReplicaListAsync",
                    () => ExceptionsHelper.TranslateCancellations(
                        func: static state => state.queryClient.GetReplicaListAsync(
                            partitionId: state.partitionId,
                            continuationToken: state.continuationToken,
                            timeout: state.eachApiCallTimeout,
                            cancellationToken: state.cancellationToken),
                        state: (this.queryClient, partitionId, eachApiCallTimeout, continuationToken, cancellationToken),
                        cancellationToken),
                    new[]
                    {
                        KeyValuePair.Create(nameof(partitionId), partitionId.ToString()),
                        KeyValuePair.Create("page", (pageIndex++).ToString()),
                    });

                foreach (var item in page)
                {
                    yield return new ReplicaWrapper
                    {
                        Id = item.Id,
                        ReplicaAddress = item.ReplicaAddress,
                        ReplicaStatus = item.ReplicaStatus,
                        HealthState = item.HealthState,
                        ServiceKind = item.ServiceKind,
                        Role = item.ServiceKind == ServiceKind.Stateful ? ((StatefulServiceReplica)item).ReplicaRole : (ReplicaRole?)null,
                    };
                }

                continuationToken = page.ContinuationToken;
            }
            while (!string.IsNullOrEmpty(continuationToken));
        }

        private static IReadOnlyDictionary<string, string> MapAppParameters(ApplicationParameterList parameters)
        {
            return parameters.ToDictionary(param => param.Name, param => param.Value, StringComparer.Ordinal);
        }
    }
}
