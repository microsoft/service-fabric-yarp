// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common.Abstractions.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;

namespace IslandGateway.FabricDiscovery
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance.
    /// </summary>
    internal sealed class StatefulServiceAdapter : StatefulService
    {
        private readonly IStatefulServiceWrapper wrapped;
        private readonly ILogger<StatefulServiceAdapter> logger;
        private readonly IOperationLogger operationLogger;

        public StatefulServiceAdapter(
            StatefulServiceContext context,
            ILogger<StatefulServiceAdapter> logger,
            IOperationLogger operationLogger,
            IStatefulServiceWrapper wrappedService)
            : base(context)
        {
            this.wrapped = wrappedService ?? throw new ArgumentNullException(nameof(wrappedService));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
        }

        protected override Task RunAsync(CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteRootAsync(
                $"{this.wrapped.GetType().FullName}.{nameof(this.wrapped.RunAsync)}",
                () => this.wrapped.RunAsync(cancellation));
        }

        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            const string EndpointName = "ServiceEndpoint";

            return new ServiceReplicaListener[]
            {
                new ServiceReplicaListener(
                    createCommunicationListener: serviceContext =>
                        new KestrelCommunicationListener(serviceContext, EndpointName, (url, listener) =>
                        {
                            this.logger.LogInformation($"Opening listener on {url}");

                            return this.wrapped.CreateWebHostBuilder()
                                .UseUrls(url) // TODO: Use SF integration middlewares to include partition id/replica id in url.
                                .Build();
                        }),
                    name: EndpointName),
            };
        }
    }
}
