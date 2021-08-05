// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using IslandGateway.RemoteConfig.Infra;
using Microsoft.Extensions.Logging;

namespace IslandGateway.Hosting.Common
{
    internal class RemoteConfigClientFactory : IRemoteConfigClientFactory
    {
        public RemoteConfigClientFactory(ILoggerFactory loggerFactory)
        {
        }

        public HttpMessageInvoker CreateClient()
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = false,
                MaxConnectionsPerServer = 1,
            };

            return new HttpMessageInvoker(handler);
        }
    }
}
