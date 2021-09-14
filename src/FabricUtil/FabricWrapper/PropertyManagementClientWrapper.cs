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

                previousResult = await FabricCallHelper.RunWithExponentialRetries(
                    (attempt, cancellationToken) => this.operationLogger.ExecuteAsync(
                        "FabricApi.EnumeratePropertiesAsync",
                        () => this.propertyManagementClient.EnumeratePropertiesAsync(
                            name: name,
                            includeValues: true,
                            previousResult: previousResult,
                            timeout: eachApiTimeout,
                            cancellationToken: cancellationToken),
                        new[]
                        {
                            KeyValuePair.Create(nameof(name), name.ToString()),
                            KeyValuePair.Create("page", pageIndex.ToString()),
                            KeyValuePair.Create("attempt", attempt.ToString()),
                        }),
                    FabricExponentialRetryPolicy.Default,
                    cancellationToken);
                foreach (NamedProperty p in previousResult)
                {
                    namedProperties[p.Metadata.PropertyName] = p.GetValue<string>();
                }

                pageIndex++;
            }
            while (previousResult.HasMoreData);
            return namedProperties;
        }
    }
}
