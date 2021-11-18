// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace Yarp.ServiceFabric.Core.Service.Security.ServerCertificateBinding
{
    /// <summary>
    /// Enumerates certificates that might be viable for use as TLS server authentication.
    /// </summary>
    internal interface ICertificateLoader
    {
        /// <summary>
        /// Lists certificates that are viable for use as TLS server authentication.
        /// </summary>
        List<X509Certificate2> LoadCandidateCertificates(StoreName storeName, StoreLocation storeLocation);
    }
}