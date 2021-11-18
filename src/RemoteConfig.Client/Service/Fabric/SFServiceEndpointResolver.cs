// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Services.Client;
using Microsoft.ServiceFabric.Services.Communication;
using Yarp.ServiceFabric.RemoteConfig.Infra;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.RemoteConfig.Fabric
{
    /// <summary>
    /// Abstracts away logic to produce a concrete Uri that can be called to reach a Service Fabric service.
    /// </summary>
    internal class SFServiceEndpointResolver : IRemoteConfigEndpointResolver
    {
        private const string RelativeUri = "api/v1/yarpconfig";

        private readonly Uri serviceName;
        private readonly IServicePartitionResolver servicePartitionResolver;
        private readonly ILogger<SFServiceEndpointResolver> logger;
        private readonly Random random = new Random();

        private ResolvedServicePartition lastResolvedServicePartition;

        /// <summary>
        /// Initializes a new instance of the <see cref="SFServiceEndpointResolver"/> class.
        /// </summary>
        public SFServiceEndpointResolver(Uri serviceName, IServicePartitionResolver servicePartitionResolver, ILogger<SFServiceEndpointResolver> logger)
        {
            this.serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
            this.servicePartitionResolver = servicePartitionResolver ?? throw new ArgumentNullException(nameof(servicePartitionResolver));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the endpoint that should be tried for reaching the desired service.
        /// </summary>
        /// <remarks>
        /// Calling this method multiple times may produce different results e.g. when the destination service replica moves
        /// or if multiple elligible endpoints are avaialble.
        /// </remarks>
        public async Task<Uri> TryGetNextEndpoint(CancellationToken cancellation)
        {
            try
            {
                if (this.lastResolvedServicePartition != null)
                {
                    this.lastResolvedServicePartition = await this.servicePartitionResolver.ResolveAsync(
                        this.lastResolvedServicePartition,
                        resolveTimeoutPerTry: ServicePartitionResolver.DefaultResolveTimeout,
                        maxRetryBackoffInterval: ServicePartitionResolver.DefaultMaxRetryBackoffInterval,
                        cancellation);
                }
                else
                {
                    this.lastResolvedServicePartition = await this.servicePartitionResolver.ResolveAsync(
                        this.serviceName,
                        ServicePartitionKey.Singleton,
                        resolveTimeoutPerTry: ServicePartitionResolver.DefaultResolveTimeout,
                        maxRetryBackoffInterval: ServicePartitionResolver.DefaultMaxRetryBackoffInterval,
                        cancellation);
                }
            }
            catch (FabricException ex)
            {
                this.logger.LogInformation(ex, $"FabricException while attempting to resolve service {this.serviceName}.");
                return null;
            }

            var endpoints = this.lastResolvedServicePartition.Endpoints?.ToList();
            if (endpoints == null || endpoints.Count == 0)
            {
                this.logger.LogInformation($"No endpoints for service {this.serviceName}.");
                return null;
            }

            // Pick any endpoint to determine the service type
            ResolvedServiceEndpoint targetEndpoint;
            if (endpoints[0].Role == ServiceEndpointRole.Stateless)
            {
                // If stateless then pick any random endpoint
                targetEndpoint = endpoints[this.random.Next(endpoints.Count)];
            }
            else
            {
                targetEndpoint = endpoints.FirstOrDefault(e => e.Role == ServiceEndpointRole.StatefulPrimary);
                if (targetEndpoint == null)
                {
                    this.logger.LogInformation($"No primary endpoint for stateful service {this.serviceName}.");
                    return null;
                }
            }

            // targetEndpoint is still a collection of named endpoints
            ServiceEndpointCollection serviceEndpointCollection;
            if (!ServiceEndpointCollection.TryParseEndpointsString(targetEndpoint.Address, out serviceEndpointCollection))
            {
                this.logger.LogInformation($"Failed to parse replica address for service {this.serviceName}: {targetEndpoint.Address}");
                return null;
            }

            // TODO: Security: only allow connecting to https endpoints
            var endpointDescriptor = new FabricServiceEndpoint(
                listenerNames: null,
                emptyStringMatchesAnyListener: true);
            if (!FabricServiceEndpointSelector.TryGetEndpoint(endpointDescriptor, serviceEndpointCollection, out Uri endpointUri))
            {
                this.logger.LogInformation($"No suitable endpoint found. Replica address: {targetEndpoint.Address}");
                return null;
            }

            return new Uri(endpointUri, RelativeUri);
        }
    }
}
