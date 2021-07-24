// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric.Description;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.ServiceFabricIntegration;

namespace IslandGateway.FabricDiscovery.FabricWrapper
{
    /// <summary>
    /// Wraps Service Fabric client SDK QueryClient api's.
    /// <see href="https://docs.microsoft.com/en-us/dotnet/api/system.fabric.fabricclient.queryclient?view=azure-dotnet"/>.
    /// </summary>
    public interface IQueryClientWrapper
    {
        /// <summary>
        /// Gets all application types.
        /// </summary>
        IAsyncEnumerable<ApplicationTypeWrapper> GetApplicationTypesAsync(PagedApplicationTypeQueryDescription query, TimeSpan eachApiCallTimeout, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all applications according to the provided <paramref name="query"/>.
        /// This is intended to be used to list all applications of a given Application Type.
        /// </summary>
        IAsyncEnumerable<ApplicationWrapper> GetApplicationsAsync(ApplicationQueryDescription query, TimeSpan eachApiCallTimeout, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the application name for a given <paramref name="serviceName"/>.
        /// </summary>
        Task<ApplicationNameKey> GetApplicationNameAsync(ServiceNameKey serviceName, TimeSpan timeout, CancellationToken cancellationToken);

        /// <summary>
        /// Gets all Service Types defined in a given Application Type Name and Version.
        /// </summary>
        IAsyncEnumerable<ServiceTypeWrapper> GetServiceTypesAsync(ApplicationTypeNameKey applicationTypeName, ApplicationTypeVersionKey applicationTypeVersion, TimeSpan eachApiCallTimeout, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all services in an application according to the provided <paramref name="query"/>.
        /// </summary>
        IAsyncEnumerable<ServiceWrapper> GetServicesAsync(ServiceQueryDescription query, TimeSpan eachApiCallTimeout, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all partitions of a given service.
        /// </summary>
        IAsyncEnumerable<PartitionWrapper> GetPartitionsAsync(ServiceNameKey serviceName, TimeSpan eachApiCallTimeout, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all replicas in a partition.
        /// </summary>
        IAsyncEnumerable<ReplicaWrapper> GetReplicasAsync(Guid partitionId, TimeSpan eachApiCallTimeout, CancellationToken cancellationToken = default);
    }
}