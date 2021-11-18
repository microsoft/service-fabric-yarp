// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig
{
    /// <summary>
    /// Provides a method to parse the XML contents of a Service Fabric ServiceManifest Extension.
    /// </summary>
    internal interface IExtensionLabelsParser
    {
        /// <summary>
        /// Gets the labels from the extensions of the provided raw service manifest.
        /// </summary>
        bool TryExtractLabels(string extensionXml, out Dictionary<string, string> labels);
    }
}