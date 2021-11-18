// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;

namespace Yarp.ServiceFabric.Core.Abstractions
{
    /// <summary>
    /// Provides a method <see cref="SelectCertificate(ConnectionContext, string)"/> to enable SNI-based TLS server certificate selection.
    /// </summary>
    /// <remarks>
    /// This is intended for use with servers such as Kestrel which support custom server certificate selection.
    /// Configuration can be performed as follows:
    /// <code>
    /// [!<![CDATA[
    /// // In Program.Main():
    ///
    /// // ...
    /// ISniServerCertificateSelector certSelector = GetInstance(); // E.g. get from Dependency Injection
    /// webBuilder.ConfigureKestrel(
    ///     kestrelOptions =>
    ///     {
    ///         kestrelOptions.ConfigureHttpsDefaults(
    ///             httpsOptions =>
    ///             {
    ///                 httpsOptions.ServerCertificateSelector = (connectionContext, hostName) => certSelector.SelectCertificate(hostName);
    ///             });
    ///     });
    /// // ...
    /// ]]>
    /// </code>
    /// </remarks>
    public interface ISniServerCertificateSelector
    {
        /// <summary>
        /// Expose certificates for logging purpose.
        /// </summary>
        IReadOnlyDictionary<string, X509Certificate2> CertificatesDict { get; }

        /// <summary>
        /// Expose wildcard certificates for logging purpose.
        /// </summary>
        IReadOnlyDictionary<string, X509Certificate2> WildcardCertificatesDict { get; }

        /// <summary>
        /// Updates the set of supported SNI host names per the provided <paramref name="options"/>.
        /// This can be called periodically in the background to refresh the list of available certificates.
        /// </summary>
        Task UpdateAsync(SniServerCertificateSelectorOptions options);

        /// <summary>
        /// Selects a TLS server authentication certificate (if available) for the specified inbound TLS SNI host name.
        /// This enables Island Gateway to serve multiple hosts on the same IP address.
        /// </summary>
        X509Certificate2 SelectCertificate(ConnectionContext connectionContext, string hostName);
    }
}
