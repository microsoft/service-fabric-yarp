// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography.X509Certificates;

namespace Yarp.ServiceFabric.Core.Abstractions
{
    /// <summary>
    /// Options consumed by implementations of <see cref="ISniServerCertificateSelector"/>.
    /// </summary>
    public class SniServerCertificateSelectorOptions
    {
        /// <summary>
        /// Which <see cref="StoreName"/> to load certificates from.
        /// Defaults to <see cref="StoreName.My"/>.
        /// </summary>
        public StoreName StoreName { get; set; } = StoreName.My;

        /// <summary>
        /// Which <see cref="StoreLocation"/> to load certificates from.
        /// Defaults to <see cref="StoreLocation.LocalMachine"/>.
        /// </summary>
        public StoreLocation StoreLocation { get; set; } = StoreLocation.LocalMachine;
    }
}
