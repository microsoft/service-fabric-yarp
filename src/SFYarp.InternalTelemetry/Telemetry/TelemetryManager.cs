// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Fabric;
using System.Fabric.Description;
using System.Fabric.Health;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Yarp.ServiceFabric.InternalTelemetry
{
    public class TelemetryManager : BackgroundService
    {
        private readonly string nodeName;

        private readonly string logFolderBasePath;

        private readonly TimeSpan operationalTelemetryRunInterval = TimeSpan.FromDays(1);
        private readonly ILogger<TelemetryManager> logger;

        private DateTime startDateTime;

        private static int TelemetryExecutionLoopSleepSeconds { get; set; } = TelemetryManagerConstants.TelemetryRunLoopSleepTimeSeconds;

        public static FabricClient FabricClientInstance { get; set; }

        public static bool TelemetryEnabled { get; set; }

        public static StatelessServiceContext FabricServiceContext { get; set; }

        private DateTime LastTelemetrySendDate { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TelemetryManager"/> class.
        /// </summary>
        public TelemetryManager(ILogger<TelemetryManager> logger, StatelessServiceContext serviceContext)
        {
            this.logger = logger;

            FabricClientInstance = new FabricClient();
            FabricServiceContext = serviceContext;

            this.nodeName = FabricServiceContext.NodeContext.NodeName;

            this.SetPropertiesFromConfigurationParameters();

            string observerLogPath = GetConfigSettingValue(TelemetryManagerConstants.TelemetryLogPath, null);

            if (!string.IsNullOrEmpty(observerLogPath))
            {
                this.logFolderBasePath = observerLogPath;
            }
            else
            {
                string logFolderBase = Path.Combine(Environment.CurrentDirectory, "TelemetryLogs");
                this.logFolderBasePath = logFolderBase;
            }

        }
        private static string GetConfigSettingValue(string parameterName, ConfigurationSettings settings)
        {
            try
            {
                ConfigurationSettings configSettings = null;

                if (settings != null)
                {
                    configSettings = settings;
                }
                else
                {
                    configSettings = FabricServiceContext.CodePackageActivationContext?.GetConfigurationPackageObject("Config")?.Settings;
                }

                var section = configSettings?.Sections[TelemetryManagerConstants.TelemetryManagerConfigurationSectionName];
                var parameter = section?.Parameters[parameterName];

                return parameter?.Value;
            }
            catch (Exception e) when (e is KeyNotFoundException || e is FabricElementNotFoundException)
            {
            }

            return null;
        }

        /// <summary>
        /// Sets TelemetryManager's related properties/fields to their corresponding Settings.xml or ApplicationManifest.xml (Overrides)
        /// configuration settings (parameter values).
        /// </summary>
        private void SetPropertiesFromConfigurationParameters(ConfigurationSettings settings = null)
        {
            // FabricObserver operational telemetry (No PII) - Override
            if (bool.TryParse(GetConfigSettingValue(TelemetryManagerConstants.EnableTelemetry, settings), out bool telemetryEnabled))
            {
                TelemetryEnabled = telemetryEnabled;
            }

        }

        // Long running background task to collect telemetry data every day
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            this.startDateTime = DateTime.UtcNow;

            try
            {
                // Continue running until a shutdown signal is sent
                this.logger.LogInformation("Starting internal Telemetry event collection");

                // Observers run sequentially. See RunObservers impl.
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Identity-agnostic internal operational telemetry sent to Service Fabric team (only) for use in
                    // understanding generic behavior of FH in the real world (no PII). This data is sent once a day and will be retained for no more
                    // than 90 days.
                    if (TelemetryEnabled && DateTime.UtcNow.Subtract(this.LastTelemetrySendDate) >= this.operationalTelemetryRunInterval)
                    {
                        try
                        {
                            this.logger.LogInformation("Sending Telemetry event to Application Insight");
                            using var telemetryEvents = new TelemetryEvents(FabricClientInstance, FabricServiceContext, stoppingToken);

                            string fileName = "SFYarpTelemetry.log";
                            string logFolderBase = Path.Combine(this.logFolderBasePath, fileName);
                            var logFolderBasePath = logFolderBase;
                            var telemetryDataSent = telemetryEvents.SendSFYarpInternalTelemetryData(logFolderBasePath);

                            if (telemetryDataSent)
                            {
                                this.logger.LogInformation("Telemetry event was successfuly sent to Application Insight");
                                this.logger.LogInformation($"Telemetry event saved locally in the following path: {logFolderBasePath}");
                                this.LastTelemetrySendDate = DateTime.UtcNow;
                            }
                            else
                            {
                                this.logger.LogInformation("Telemetry event NOT sent to Application Insight");
                            }
                        }
                        catch
                        {
                            // Telemetry is non-critical and should not take down SFYarp service
                            // TelemetryLib will log exception details to file in top level Telemetry log folder.
                        }
                    }

                    if (TelemetryExecutionLoopSleepSeconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(TelemetryExecutionLoopSleepSeconds), stoppingToken);
                    }
                }
            }
            catch (Exception e)
            {
                var message =
                    $"Unhandled Exception in {TelemetryManagerConstants.TelemetryManagerName} on node " +
                    $"{this.nodeName}. Taking down Telemetry process. " +
                    $"Error info:{Environment.NewLine}{e}";

                this.logger.LogError(message);

                // Don't swallow the exception.
                // Take down Telemetry process. Fix the bug(s).
                throw;
            }
        }
    }
}
