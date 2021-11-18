// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Yarp.ServiceFabric.CoreServicesBorrowed.CoreFramework;

namespace Yarp.ServiceFabric.Core.Service.Security.ServerCertificateBinding
{
    /// <summary>
    /// Helper class to enumerate and load certificates.
    /// </summary>
    /// <remarks>
    /// Based on Kestrel's <c>CertificateLoader</c> class.
    /// See: <see href="https://github.com/dotnet/aspnetcore/blob/2e7c52d97c1aa9b372d740fd46accfd6e2d5a97b/src/Servers/Kestrel/Core/src/CertificateLoader.cs"/>.
    /// </remarks>
    internal sealed class CertificateLoader : ICertificateLoader
    {
        // See http://oid-info.com/get/1.3.6.1.5.5.7.3.1
        // Indicates that a certificate can be used as a SSL server certificate
        private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

        private readonly ILogger<CertificateLoader> logger;

        public CertificateLoader(ILogger<CertificateLoader> logger)
        {
            Contracts.CheckValue(logger, nameof(logger));
            this.logger = logger;
        }

        /// <inheritdoc/>
        public List<X509Certificate2> LoadCandidateCertificates(StoreName storeName, StoreLocation storeLocation)
        {
            this.logger.LogInformation($"Loading certs from StoreName={storeName}, StoreLocation={storeLocation}");
            using (var store = new X509Store(storeName, storeLocation))
            {
                store.Open(OpenFlags.ReadOnly);
                var foundCertificates = store.Certificates.Find(X509FindType.FindByTimeValid, DateTime.Now, validOnly: true);

                var goodCertificates = new List<X509Certificate2>();
                foreach (var certificate in foundCertificates)
                {
                    var sans = string.Join(", ", CertificateSanHelper.GetSanDnsNames(certificate));

                    // NOTE 1: Requiring online certificate revocation checks could lead to an outage if we are unable to check the revocation list...
                    //         Any service requiring online certificate revocation is subject to this issue.
                    // NOTE 2: We deliberately do not enforce X509Certificate2.PrivateKey != null here, because that is used only as a disambiguating factor,
                    //         and is not a necessary condition. The caller is supposed to pick the best cert among the available ones.
                    if (IsCertificateAllowedForServerAuth(certificate) &&
                        certificate.HasPrivateKey &&
                        certificate.Verify()) // Performs chain validation + Online revocation check. `validOnly = true` above is not enough.
                    {
                        this.logger.LogInformation($"Found good cert for {sans}: {certificate}");
                        goodCertificates.Add(certificate);
                    }
                    else
                    {
                        this.logger.LogInformation($"Found bad cert for {sans}: {certificate}");
                        certificate.Dispose();
                    }
                }

                return goodCertificates;
            }
        }

        private static bool IsCertificateAllowedForServerAuth(X509Certificate2 certificate)
        {
            /* If the Extended Key Usage extension is included, then we check that the serverAuth usage is included. (http://oid-info.com/get/1.3.6.1.5.5.7.3.1)
             * If the Extended Key Usage extension is not included, then we assume the certificate is allowed for all usages.
             *
             * See also https://blogs.msdn.microsoft.com/kaushal/2012/02/17/client-certificates-vs-server-certificates/
             *
             * From https://tools.ietf.org/html/rfc3280#section-4.2.1.13 "Certificate Extensions: Extended Key Usage"
             *
             * If the (Extended Key Usage) extension is present, then the certificate MUST only be used
             * for one of the purposes indicated.  If multiple purposes are
             * indicated the application need not recognize all purposes indicated,
             * as long as the intended purpose is present.  Certificate using
             * applications MAY require that a particular purpose be indicated in
             * order for the certificate to be acceptable to that application.
             */

            var hasEkuExtension = false;

            foreach (var extension in certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>())
            {
                hasEkuExtension = true;
                foreach (var oid in extension.EnhancedKeyUsages)
                {
                    if (oid.Value.Equals(ServerAuthenticationOid, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return !hasEkuExtension;
        }
    }
}
