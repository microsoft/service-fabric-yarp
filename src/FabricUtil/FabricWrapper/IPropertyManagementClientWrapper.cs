// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Yarp.ServiceFabric.FabricDiscovery.FabricWrapper
{
    /// <summary>
    /// Wraps Service Fabric client SDK PropertyManagement api's.
    /// <see href="https://docs.microsoft.com/en-us/dotnet/api/system.fabric.fabricclient.propertymanagementclient?view=azure-dotnet"/>..
    /// </summary>
    public interface IPropertyManagementClientWrapper
    {
        /// <summary>
        /// Enumerates all Service Fabric properties under a given name.
        /// Also takes in timeout interval, which is the maximum of time the system will allow this operation to continue before returning.
        /// </summary>
        Task<Dictionary<string, string>> EnumeratePropertiesAsync(Uri name, TimeSpan eachApiTimeout, CancellationToken cancellationToken);
    }
}