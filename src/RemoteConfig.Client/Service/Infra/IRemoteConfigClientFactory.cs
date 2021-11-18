// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Http;

namespace Yarp.ServiceFabric.RemoteConfig.Infra
{
    /// <summary>
    /// Provides a method to create an <see cref="HttpMessageInvoker"/> to call the remote configuration server.
    /// </summary>
    public interface IRemoteConfigClientFactory
    {
        /// <summary>
        /// Creates an <see cref="HttpMessageInvoker"/> to call the remote configuration server.
        /// </summary>
        HttpMessageInvoker CreateClient();
    }
}
