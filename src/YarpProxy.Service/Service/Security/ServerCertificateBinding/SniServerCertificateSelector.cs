// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using Yarp.ServiceFabric.Common.Util;
using Yarp.ServiceFabric.Core.Abstractions;
using Yarp.ServiceFabric.CoreServicesBorrowed.CoreFramework;

namespace Yarp.ServiceFabric.Core.Service.Security.ServerCertificateBinding
{
    /// <summary>
    /// Default implementation of <see cref="ISniServerCertificateSelector"/>.
    /// </summary>
    internal sealed class SniServerCertificateSelector : ISniServerCertificateSelector
    {
        private readonly ILogger<SniServerCertificateSelector> logger;
        private readonly ICertificateLoader certificateLoader;

        private volatile IReadOnlyDictionary<string, X509Certificate2> certificatesDict = new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);
        private volatile IReadOnlyDictionary<string, X509Certificate2> wildcardCertificatesDict = new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);

        public SniServerCertificateSelector(ILogger<SniServerCertificateSelector> logger, ICertificateLoader certificateLoader)
        {
            Contracts.CheckValue(logger, nameof(logger));
            Contracts.CheckValue(certificateLoader, nameof(certificateLoader));

            this.logger = logger;
            this.certificateLoader = certificateLoader;
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, X509Certificate2> CertificatesDict => this.certificatesDict;

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, X509Certificate2> WildcardCertificatesDict => this.wildcardCertificatesDict;

        /// <inheritdoc/>
        public X509Certificate2 SelectCertificate(ConnectionContext connectionContext, string hostName)
        {
            this.logger.LogInformation($"ConnectionId: '{connectionContext?.ConnectionId}', SNI host name: '{hostName}'");

            if (string.IsNullOrEmpty(hostName))
            {
                return null;
            }

            var certificates = this.certificatesDict;
            if (certificates.TryGetValue(hostName, out var certificate))
            {
                // Exact match, return this certificate
                return certificate;
            }

            // See if we have a wildcard cert that supports the given hostName
            int firstDotIndex = hostName.IndexOf('.');
            if (firstDotIndex > 0 && firstDotIndex < hostName.Length - 1)
            {
                string withoutFirstLabel = hostName.Substring(firstDotIndex + 1);
                var wildcardCertificates = this.wildcardCertificatesDict;
                if (wildcardCertificates.TryGetValue(withoutFirstLabel, out var wildcardCertificate))
                {
                    // Found a matching wildcard cert
                    return wildcardCertificate;
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public Task UpdateAsync(SniServerCertificateSelectorOptions options)
        {
            Contracts.CheckValue(options, nameof(options));

            var newCertificatesDict = new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);
            var newWildcardCertificatesDict = new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);

            var certificates = this.certificateLoader.LoadCandidateCertificates(options.StoreName, options.StoreLocation);

            // Give preference to certs that definitively have an accessible private key,
            // then disambiguate by picking the ones issued most recently.
            // This ensures that, during a cert rotation, we would not pick a new cert
            // before its private key has been properly ACL'd to our process.
            var orderedCertificates = certificates
                .OrderByDescending(c => this.HasAccessToPrivateKey(c))
                .ThenByDescending(c => c.NotBefore);
            foreach (var certificate in orderedCertificates)
            {
                // If a certificate has multiple DNS Names, we will add multiple entries to the dictionary
                // all pointing at the same certificate.
                foreach (var dnsName in CertificateSanHelper.GetSanDnsNames(certificate))
                {
                    if (dnsName.StartsWith("*.", StringComparison.Ordinal))
                    {
                        newWildcardCertificatesDict.TryAdd(dnsName.Substring(2), certificate);
                    }
                    else
                    {
                        newCertificatesDict.TryAdd(dnsName, certificate);
                    }
                }
            }

            var curCertificatesDict = this.certificatesDict;
            var curWildcardCertificatesDict = this.wildcardCertificatesDict;

            bool changed = false;
            changed |= !AreEquivalentAndConsolidate(curCertificatesDict, newCertificatesDict);
            changed |= !AreEquivalentAndConsolidate(curWildcardCertificatesDict, newWildcardCertificatesDict);

            if (!changed)
            {
                // Dispose all certs we just enumerated, it was in vain...
                foreach (var certificate in certificates)
                {
                    certificate.Dispose();
                }

                return Task.CompletedTask;
            }

            this.logger.LogInformation($"Certificates changed. Applying new bindings for {newCertificatesDict.Count} DNS names, {newWildcardCertificatesDict.Count} wildcard names.");
            this.certificatesDict = newCertificatesDict;
            this.wildcardCertificatesDict = newWildcardCertificatesDict;

            // Dispose unused certificates from the current round.
            // E.g. maybe there were multiple valid certificates for the same DNS name
            // and we only used the one with the farthest expiration date,
            // or we re-used certificates we had loaded prior and can therefore safely dispose the new ones.
            // See also method `AreEquivalentAndConsolidate` below.
            // NOTE: Use of ReferenceEqualityComparer ensures we reason about certificate object instances
            // and never dispose the wrong certificate just because one may be equivalent to another instance.
            var consumedCertificates = new HashSet<X509Certificate2>(ReferenceEqualityComparer<X509Certificate2>.Default);
            foreach (var kvp in newCertificatesDict)
            {
                consumedCertificates.Add(kvp.Value);
            }

            foreach (var kvp in newWildcardCertificatesDict)
            {
                consumedCertificates.Add(kvp.Value);
            }

            foreach (var certificate in certificates)
            {
                if (!consumedCertificates.Contains(certificate))
                {
                    certificate.Dispose();
                }
            }

            // NOTE: At some point we should dispose old certificates that are no longer in use.
            // But how do we know when they are really no longer in use?
            // Because certificates generally change infrequently and we only "leak" certificates
            // when they are removed or updated, we accept the risk of leaking those instances.
            return Task.CompletedTask;
        }

        /// <summary>
        /// Compares the dictionaries, and returns <c>true</c> if they are equivalent.
        /// Additionally, values that represent the same certificate in <paramref name="old"/> and <paramref name="updated"/>
        /// are consolidated, so that the entry in <paramref name="updated"/> is modified to point at the same instance
        /// that was used in <paramref name="old"/>.
        /// </summary>
        private static bool AreEquivalentAndConsolidate(IReadOnlyDictionary<string, X509Certificate2> old, Dictionary<string, X509Certificate2> updated)
        {
            bool equal = true;

            if (old.Count != updated.Count)
            {
                equal = false;
            }

            // It suffices to scan the left side only since we already checked if the count changed,
            // and if the count hasn't changed then we can scan from either side.
            foreach (var kvp in old)
            {
                if (!updated.TryGetValue(kvp.Key, out var valueB))
                {
                    equal = false;
                    continue;
                }

                // NOTE: X509Certificate2.Thumbprint is the SHA1 digest of the certificate.
                // While use of SHA1 is banned, its use here is not for crypto purposes,
                // and the certs we are operating on below are all trustworthy already.
                if (kvp.Value.Thumbprint != valueB.Thumbprint)
                {
                    equal = false;
                    continue;
                }

                // If we get here, then both `old` and `updated` are pointing at the same certificate,
                // even if they are different instances. So consolidate.
                // This way, we can then dispose the one originally in `updated` in favor of the old one we already had.
                updated[kvp.Key] = kvp.Value;
            }

            return equal;
        }

        // NOTE: Taken from https://msazure.visualstudio.com/One/_git/CoreFramework?path=%2Fsrc%2FCoreFramework%2FCoreFramework%2FSecurity%2FCertificateManager.cs&version=GBmaster&line=139&lineEnd=156&lineStartColumn=1&lineEndColumn=1&lineStyle=plain
        private bool HasAccessToPrivateKey(X509Certificate2 certificate)
        {
            try
            {
                // We tried to use `SignedCms.ComputeSignature` for an arbitrary data with `CmsSigner` constructed with certificate
                // in order to validate we can use the private key with current user. That will leverage windows security api to achieve that even when the cert is non exportable.
                // However based on https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.pkcs.signedcms.computesignature?view=netframework-4.6.2,
                // it seems that this can result in pin prompt even we set silent to true for some situation.
                // This could be a potential issue which can potentially result in outage. We choose to access PrivateKey directly for verification.
                return certificate.HasPrivateKey;
            }
            catch (Exception ex) when (ex is CryptographicException || ex is NotSupportedException)
            {
                this.logger.LogWarning($"Encountered error when tried to validate private key access for certificate with subject name: {certificate.Subject}, thumbprint: {certificate.Thumbprint}, ex: {ex}");
                return false;
            }
        }
    }
}
