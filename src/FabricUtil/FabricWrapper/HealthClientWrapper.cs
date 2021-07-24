// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Fabric;
using System.Fabric.Health;

namespace IslandGateway.ServiceFabricIntegration
{
    /// <inheritdoc/>
    public class HealthClientWrapper : IHealthClientWrapper
    {
        private readonly FabricClient.HealthClient healthClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="HealthClientWrapper" /> class.
        /// </summary>
        public HealthClientWrapper()
        {
            this.healthClient = new FabricClient().HealthManager;
        }

        /// <inheritdoc/>
        public void ReportHealth(HealthReport healthReport, HealthReportSendOptions sendOptions)
        {
            this.healthClient.ReportHealth(healthReport, sendOptions);
        }
    }
}
