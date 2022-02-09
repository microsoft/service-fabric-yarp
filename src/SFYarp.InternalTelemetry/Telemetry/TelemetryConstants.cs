// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Yarp.ServiceFabric.InternalTelemetry
{
    internal static class TelemetryConstants
    {
        internal const int AsyncOperationTimeoutSeconds = 120;
        internal const string Undefined = "undefined";
        internal const string ClusterTypeStandalone = "standalone";
        internal const string ClusterTypeSfrp = "SFRP";
        internal const string ClusterTypePaasV1 = "PaasV1";
        internal const string ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
    }
}