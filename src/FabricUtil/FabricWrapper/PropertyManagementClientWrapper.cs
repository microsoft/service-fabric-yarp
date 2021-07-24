// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common.Abstractions.Telemetry;
using IslandGateway.ServiceFabricIntegration;

namespace IslandGateway.FabricDiscovery.FabricWrapper
{
    /// <summary>
    /// A wrapper class for the service fabric client SDK.
    /// See Microsoft documentation: https://docs.microsoft.com/en-us/dotnet/api/system.fabric.fabricclient.propertymanagementclient?view=azure-dotnet .
    /// </summary>
    public class PropertyManagementClientWrapper : IPropertyManagementClientWrapper
    {
        private readonly IOperationLogger operationLogger;

        /// <summary>
        /// Represents the property management client used to perform management of names and properties.
        /// </summary>
        private readonly FabricClient.PropertyManagementClient propertyManagementClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertyManagementClientWrapper"/> class.
        /// </summary>
        public PropertyManagementClientWrapper(IOperationLogger operationLogger)
        {
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
            this.propertyManagementClient = new FabricClient().PropertyManager;
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, string>> EnumeratePropertiesAsync(Uri name, TimeSpan eachApiTimeout, CancellationToken cancellationToken)
        {
            _ = name ?? throw new ArgumentNullException(nameof(name));

            var namedProperties = new Dictionary<string, string>(StringComparer.Ordinal);
            PropertyEnumerationResult previousResult = null;

            int pageIndex = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                previousResult = await this.operationLogger.ExecuteAsync(
                    "FabricApi.EnumeratePropertiesAsync",
                    () => ExceptionsHelper.TranslateCancellations(
                        func: static state => state.propertyManagementClient.EnumeratePropertiesAsync(
                            name: state.name,
                            includeValues: true,
                            previousResult: state.previousResult,
                            timeout: state.eachApiTimeout,
                            cancellationToken: state.cancellationToken),
                        state: (this.propertyManagementClient, name, previousResult, eachApiTimeout, cancellationToken),
                        cancellation: cancellationToken),
                    new[]
                    {
                        KeyValuePair.Create(nameof(name), name.ToString()),
                        KeyValuePair.Create("page", (pageIndex++).ToString()),
                    });
                foreach (NamedProperty p in previousResult)
                {
                    namedProperties[p.Metadata.PropertyName] = p.GetValue<string>();
                }
            }
            while (previousResult.HasMoreData);
            return namedProperties;
        }
    }
}
