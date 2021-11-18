// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Yarp.ServiceFabric.FabricDiscovery.IslandGatewayConfig
{
    internal class ExtensionLabelsParser : IExtensionLabelsParser
    {
        internal static readonly XNamespace XNSExtensionNoSchema = "http://schemas.microsoft.com/2015/03/fabact-no-schema";
        internal static readonly XName XNameLabel = XNSExtensionNoSchema + "Label";
        internal static readonly XName XNameLabels = XNSExtensionNoSchema + "Labels";

        private readonly ILogger<ExtensionLabelsParser> logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtensionLabelsParser"/> class.
        /// </summary>
        public ExtensionLabelsParser(ILogger<ExtensionLabelsParser> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool TryExtractLabels(string extensionXml, out Dictionary<string, string> labels)
        {
            _ = extensionXml ?? throw new ArgumentNullException(nameof(extensionXml));

            using var reader = XmlReader.Create(new StringReader(extensionXml), CreateSafeXmlSetting(1024 * 1024, 1024));
            XDocument parsedManifest;
            try
            {
                parsedManifest = XDocument.Load(reader, LoadOptions.None);
            }
            catch (XmlException ex)
            {
                this.logger.LogInformation(ex, "ServiceManifest Extension XML parse failed.");
                labels = null;
                return false;
            }

            var labelsElement = parsedManifest
                .Elements(XNameLabels)
                .Elements(XNameLabel);

            labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var labelElement in labelsElement)
            {
                // NOTE: Last write wins in case of duplicated labels
                labels[labelElement.Attribute("Key").Value] = labelElement.Value;
            }
            return true;
        }

        /// <summary>
        /// This class creates XmlReaderSettings providing Safe Xml parsing in the senses below:
        ///     1. DTD processing is disabled to prevent Xml Bomb.
        ///     2. XmlResolver is disabled to prevent Schema/External DTD resolution.
        ///     3. Maximum size for Xml document and entities are explicitly set. Zero for the size means there is no limit.
        ///     4. Comments/processing instructions are not allowed.
        /// </summary>
        private static XmlReaderSettings CreateSafeXmlSetting(long maxAcceptedCharsInDocument, long maxCharactersFromEntities)
        {
            return new XmlReaderSettings
            {
                Async = true,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                XmlResolver = null,
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = maxAcceptedCharsInDocument,
                MaxCharactersFromEntities = maxCharactersFromEntities,
            };
        }
    }
}
