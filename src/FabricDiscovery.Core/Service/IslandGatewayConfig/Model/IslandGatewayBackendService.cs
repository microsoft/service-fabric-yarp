// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Yarp.ServiceFabric.FabricDiscovery.Topology;

namespace Yarp.ServiceFabric.FabricDiscovery.IslandGatewayConfig
{
    internal class IslandGatewayBackendService
    {
        public TimeSpan LastUsed { get; set; }
        public DiscoveredAppEx FabricApplication { get; set; }
        public DiscoveredServiceEx FabricService { get; set; }
        public IslandGatewayParsedServiceType ParsedServiceType { get; set; }

        /// <summary>
        /// Combines the raw labels from the Service Manifest with app param replacements.
        /// This does not include the include the influence of SF Properties, see <see cref="LabelOverrides"/>.
        /// </summary>
        public IReadOnlyDictionary<string, string> EffectiveLabels { get; set; }

        /// <summary>
        /// When properties should be fetched again for this service.
        /// </summary>
        public TimeSpan? NextPropertiesFetch { get; set; }

        /// <summary>
        /// Service Fabric Naming Service Properties discovered for this service.
        /// This only gets populated for services that have opted-in to dynamic property overrides.
        /// </summary>
        public IReadOnlyDictionary<string, string> LabelOverrides { get; set; }

        /// <summary>
        /// Combines the <see cref="EffectiveLabels"/> with <see cref="LabelOverrides"/>.
        /// </summary>
        public IReadOnlyDictionary<string, string> FinalEffectiveLabels { get; set; }
    }
}
