// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace IslandGateway.FabricDiscovery
{
    internal static class Consts
    {
        internal static readonly string ServiceManifestExtensionName = "IslandGateway";
        internal static readonly TimeSpan DefaultFabricApiTimeout = TimeSpan.FromMinutes(1);
    }
}
