// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace IslandGateway.FabricDiscovery.IslandGatewayConfig
{
    /// <summary>
    /// Provides methods to get the discovered Service Fabric topology mapped to Island Gateway abstractions.
    /// </summary>
    public class IslandGatewaySerializedConfig
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IslandGatewaySerializedConfig"/> class.
        /// </summary>
        public IslandGatewaySerializedConfig(byte[] bytes, string etag, string contentType, string contentEncoding = null)
        {
            if (string.IsNullOrEmpty(etag))
            {
                throw new ArgumentNullException(nameof(etag));
            }

            if (string.IsNullOrEmpty(contentType))
            {
                throw new ArgumentNullException(nameof(contentType));
            }

            this.Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            this.ETag = etag;
            this.ContentType = contentType;
            this.ContentEncoding = contentEncoding;
        }

        /// <summary>
        /// Raw bytes.
        /// </summary>
        public byte[] Bytes { get; }

        /// <summary>
        /// Etag that identifies this version of the configuration.
        /// </summary>
        public string ETag { get; }

        /// <summary>
        /// Content type that the configuration was serialized into.
        /// </summary>
        public string ContentType { get; }

        /// <summary>
        /// Content encoding that the configuration was serialized into.
        /// </summary>
        public string ContentEncoding { get; }
    }
}
