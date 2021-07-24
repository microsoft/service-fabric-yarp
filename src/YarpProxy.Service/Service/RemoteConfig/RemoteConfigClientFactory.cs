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
                SslOptions = new SslClientAuthenticationOptions
                {
                    // TODO: davidni SF-YARP: Remove insecure defaults
                    RemoteCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true,
                },
            };

            return new HttpMessageInvoker(handler);
        }
    }
}
