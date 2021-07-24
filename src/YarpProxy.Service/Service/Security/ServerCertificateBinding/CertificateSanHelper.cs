// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using IslandGateway.CoreServicesBorrowed.CoreFramework;

namespace IslandGateway.Core.Service.Security.ServerCertificateBinding
{
    /// <summary>
    /// Helper class to extract SAN (Subject Alternative Name) DNS names from a certificate.
    /// </summary>
    internal sealed class CertificateSanHelper
    {
        public static IEnumerable<string> GetSanDnsNames(X509Certificate2 certificate)
        {
            Contracts.CheckValue(certificate, nameof(certificate));

            // "Subject Alternative Name" extensions oid. See: https://tools.ietf.org/html/rfc2459
            const string SubjectAltNameOid = "2.5.29.17";

            var sanExtensions = certificate.Extensions
                .Cast<X509Extension>()
                .Where(e => string.Equals(e.Oid.Value, SubjectAltNameOid, StringComparison.Ordinal));
            foreach (var sanExtension in sanExtensions)
            {
                // Example formatted output below on Windows 10 (yes, spaceship characters can appear in the output due to punycode):
                // DNS Name=🚀.localhost (xn--158h.localhost)
                // DNS Name=example.com
                // DNS Name=*.🚀.localhost (*.xn--158h.localhost)
                //
                // Note: see https://stackoverflow.com/a/59382929 re. differences in output between Windows and other platforms.
                // The current code may fail on platforms other than Windows by not detecting its SAN entries.
                string formatted = sanExtension.Format(multiLine: true);

                using (var reader = new StringReader(formatted))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        int equalsIndex = line.IndexOf("=", StringComparison.Ordinal);
                        if (equalsIndex <= 0 || equalsIndex == line.Length - 1)
                        {
                            continue;
                        }

                        string left = line.Substring(0, equalsIndex);
                        string right = line.Substring(equalsIndex + 1);

                        if (!string.Equals("DNS Name", left, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // Handle punycode names.
                        // Here we extract the portion in parentheses. The alternative to doing this terrible thing
                        // is to parse the raw ASN1 extendion data.
                        if (right.Contains(")", StringComparison.Ordinal))
                        {
                            // LastIndexOf because a valid DNS name cannot have parentheses, whereas the punycode-decoded portion may.
                            int open = right.LastIndexOf('(');
                            int close = right.LastIndexOf(')');
                            if (open < 0 || close < 0 || open > close)
                            {
                                continue;
                            }

                            right = right.Substring(open + 1, close - open - 1);
                        }

                        yield return right;
                    }
                }
            }
        }
    }
}
