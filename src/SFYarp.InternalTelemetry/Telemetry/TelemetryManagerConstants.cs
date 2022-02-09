// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Yarp.ServiceFabric.InternalTelemetry
{
    public sealed class TelemetryManagerConstants
    {
        // ObserverManager settings.
        public const string TelemetryManagerName = "TelemetryManager";
        public const string TelemetryManagerConfigurationSectionName = "TelemetryManagerConfiguration";
        public const string TelemetryLogPath = "TelemetryLogPath";
        public const string EnableTelemetry = "EnableTelemetry";

        // Default to 1 minute if frequency is not supplied in config
        public const int TelemetryRunLoopSleepTimeSeconds = 60;

        // Telemetry Settings Parameters.
        public const string ClusterTypeSfrp = "SFRP";
        public const string Undefined = "Undefined";
        public const string ClusterTypePaasV1 = "PaasV1";
        public const string ClusterTypeStandalone = "Standalone";
    }
}
