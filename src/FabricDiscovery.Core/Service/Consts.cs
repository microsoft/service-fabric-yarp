// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Yarp.ServiceFabric.FabricDiscovery
{
    internal static class Consts
    {
        internal static readonly string ServiceManifestExtensionName = "Yarp";
        internal static readonly TimeSpan DefaultFabricApiTimeout = TimeSpan.FromMinutes(1);
    }
}
