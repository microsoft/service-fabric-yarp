// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;

namespace Yarp.ServiceFabric.FabricDiscovery
{
    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance.
    /// </summary>
    internal sealed class StatelessServiceAdapter : StatelessService
    {
        private readonly IStatelessServiceWrapper wrapped;
        private readonly ILogger<StatelessServiceAdapter> logger;
        private readonly IOperationLogger operationLogger;

        public StatelessServiceAdapter(
            StatelessServiceContext context,
            ILogger<StatelessServiceAdapter> logger,
            IOperationLogger operationLogger,
            IStatelessServiceWrapper wrappedService)
            : base(context)
        {
            this.wrapped = wrappedService ?? throw new ArgumentNullException(nameof(wrappedService));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
        }

        /// <inheritdoc />
        protected override Task OnOpenAsync(CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteRootAsync(
                "FabricDiscovery.StatefulServiceAdapter.OnOpenAsync",
                () => base.OnOpenAsync(cancellation),
                new[]
                {
                    KeyValuePair.Create("open", "open"),
                });
        }

        /// <inheritdoc />
        protected override Task RunAsync(CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteRootAsync(
                "FabricDiscovery.StatefulServiceAdapter.RunAsync",
                () => this.wrapped.RunAsync(cancellation));
        }

        /// <inheritdoc />
        protected override Task OnCloseAsync(CancellationToken cancellation)
        {
            return this.operationLogger.ExecuteRootAsync(
                "FabricDiscovery.StatefulServiceAdapter.OnCloseAsync",
                () => base.OnCloseAsync(cancellation));
        }

        /// <inheritdoc />
        protected override void OnAbort()
        {
            this.operationLogger.ExecuteRoot(
                "FabricDiscovery.StatefulServiceAdapter.OnAbort",
                () => base.OnAbort());
        }

        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            const string EndpointName = "ServiceEndpoint";

            return this.operationLogger.Execute(
                "FabricDiscovery.StatefulServiceAdapter.CreateServiceInstanceListeners",
                () =>
                    new ServiceInstanceListener[]
                    {
                        new ServiceInstanceListener(
                            createCommunicationListener: serviceContext =>
                                new KestrelCommunicationListener(serviceContext, EndpointName, (url, listener) =>
                                {
                                    this.logger.LogInformation($"Opening listener on {url}");

                                    return this.wrapped.CreateWebHostBuilder()
                                        .UseUrls(url) // TODO: Use SF integration middlewares to include partition id/replica id in url.
                                        .Build();
                                }),
                            name: EndpointName),
                    });
        }
    }
}
