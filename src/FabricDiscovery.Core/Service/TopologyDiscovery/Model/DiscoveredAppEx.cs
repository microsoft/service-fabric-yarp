// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Yarp.ServiceFabric.FabricDiscovery.Util;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
{
    internal record DiscoveredAppEx : DiscoveredApp
    {
        private readonly Lazy<Dictionary<string, string>> effectiveAppParams;
        private readonly IReadOnlyDictionary<ServiceNameKey, DiscoveredService> services;

        public DiscoveredAppEx(DiscoveredApp app, DiscoveredAppTypeEx appType, IReadOnlyDictionary<ServiceNameKey, DiscoveredService> services)
            : base(app)
        {
            this.AppType = appType ?? throw new ArgumentNullException(nameof(appType));

            if (appType.AppType.ApplicationTypeName != app.Application.ApplicationTypeName ||
                appType.AppType.ApplicationTypeVersion != app.Application.ApplicationTypeVersion)
            {
                throw new InvalidOperationException(
                    $"Application metadata (AppTypeName='{app.Application.ApplicationTypeName}', AppTypeVersion='{app.Application.ApplicationTypeVersion}') " +
                    $"does not match app type metadata (AppTypeName='{appType.AppType.ApplicationTypeName}', AppTypeVersion='{appType.AppType.ApplicationTypeVersion}'). " +
                    "This is a coding defect.");
            }

            this.services = services ?? throw new ArgumentNullException(nameof(services));
            this.effectiveAppParams = new Lazy<Dictionary<string, string>>(this.ComputeEffectiveParams);
        }

        public DiscoveredAppTypeEx AppType { get; }
        public IReadOnlyDictionary<ServiceNameKey, DiscoveredService> Services
        {
            get => this.services;
            init => this.services = value ?? throw new ArgumentNullException(nameof(value));
        }
        public IReadOnlyDictionary<string, string> EffectiveAppParams => this.effectiveAppParams.Value;

        private Dictionary<string, string> ComputeEffectiveParams()
        {
            return DictionaryUtil.CombineDictionaries(this.AppType.AppType.DefaultParameters, this.Application.ApplicationParameters, StringComparer.Ordinal);
        }
    }
}
