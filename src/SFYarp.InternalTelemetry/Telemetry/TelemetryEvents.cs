// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Newtonsoft.Json;

namespace Yarp.ServiceFabric.InternalTelemetry
{
    /// <summary>
    /// Contains common SFYarp Telemetry events.
    /// </summary>
    public class TelemetryEvents : IDisposable
    {
        private const string SFYarpTelemetryTaskName = "YarpProxy";
        private const string SFYarpTelemetryEventName = "TelemetryEvent";
        private readonly TelemetryClient telemetryClient;
        private readonly ServiceContext serviceContext;
        private readonly string clusterId;
        private readonly string tenantId;
        private readonly string clusterType;
        private readonly TelemetryConfiguration appInsightsTelemetryConf;

        /// <summary>
        /// Initializes a new instance of the <see cref="TelemetryEvents"/> class.
        /// </summary>
        public TelemetryEvents(FabricClient fabricClient, ServiceContext context, CancellationToken token)
        {
            this.serviceContext = context;
            this.appInsightsTelemetryConf = TelemetryConfiguration.CreateDefault();
            this.appInsightsTelemetryConf.ConnectionString = TelemetryConstants.ConnectionString;
            this.telemetryClient = new TelemetryClient(this.appInsightsTelemetryConf);

            // Set instance fields.
            var (clusterId, tenantId, clusterType) = ClusterInformation.TupleGetClusterIdAndTypeAsync(fabricClient, token).GetAwaiter().GetResult();
            this.clusterId = clusterId;
            this.tenantId = tenantId;
            this.clusterType = clusterType;
        }

        /// <summary>
        /// Emit internal SF YarpProxy telemetry data to application insight.
        /// </summary>
        public bool SendSFYarpInternalTelemetryData(string logFilePath)
        {
            if (this.telemetryClient == null || !this.telemetryClient.IsEnabled())
            {
                return false;
            }

            try
            {
                IDictionary<string, string> eventProperties = new Dictionary<string, string>
                {
                    { "EventName", SFYarpTelemetryEventName },
                    { "TaskName", SFYarpTelemetryTaskName },
                    { "ClusterId", this.clusterId },
                    { "ClusterType", this.clusterType },
                    { "Timestamp", DateTime.UtcNow.ToString("o") },
                };
                /* TODO: Verify if this data is needed for internal telemetry
                if (eventProperties.TryGetValue("ClusterType", out string clustType))
                {
                    if (clustType != TelemetryConstants.ClusterTypeSfrp)
                    {
                        eventProperties.Add("TenantId", this.tenantId);
                    }
                }
                */

                string nodeHashString = string.Empty;
                _ = TryGetHashStringSha256(this.serviceContext.NodeContext.NodeName, out nodeHashString);
                eventProperties.Add("NodeNameHash", nodeHashString);

                this.telemetryClient.TrackEvent($"{SFYarpTelemetryTaskName}.{SFYarpTelemetryEventName}", eventProperties);
                this.telemetryClient.Flush();

                // allow time for flushing
                Thread.Sleep(1000);

                _ = this.TryWriteLogFile(logFilePath, JsonConvert.SerializeObject(eventProperties));

                return true;
            }
            catch (Exception e)
            {
                // Telemetry is non-critical and should not take down YarpProxy service
                _ = this.TryWriteLogFile(logFilePath, $"{e}");
            }
            return false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.telemetryClient.Flush();

            // allow time for flushing.
            Thread.Sleep(1000);
            this.appInsightsTelemetryConf?.Dispose();
        }

        public bool TryWriteLogFile(string path, string content, int numRetries = 4)
        {
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            for (var i = 0; i < numRetries; i++)
            {
                try
                {
                    string directory = Path.GetDirectoryName(path);

                    if (!Directory.Exists(directory))
                    {
                        if (directory != null)
                        {
                            _ = Directory.CreateDirectory(directory);
                        }
                    }

                    File.WriteAllText(path, content);
                    return true;
                }
                catch
                {
                // TODO: Log the exception msg as LogError. Directory path may not be valid
                }

                Thread.Sleep(1000);
            }

            return false;
        }

        /// <summary>
        /// Tries to compute sha256 hash of a supplied string and converts the hashed bytes to a string supplied in result.
        /// </summary>
        /// <param name="source">The string to be hashed.</param>
        /// <param name="result">The resulting Sha256 hash string. This will be null if the function returns false.</param>
        /// <returns>true if it can compute supplied string to a Sha256 hash and convert result to a string. false if it can't.</returns>
        public static bool TryGetHashStringSha256(string source, out string result)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                result = null;
                return false;
            }

            try
            {
                StringBuilder sb = new StringBuilder();

                using (var hash = SHA256.Create())
                {
                    Encoding enc = Encoding.UTF8;
                    byte[] byteVal = hash.ComputeHash(enc.GetBytes(source));

                    foreach (byte b in byteVal)
                    {
                        sb.Append(b.ToString("x2"));
                    }
                }

                result = sb.ToString();
                return true;
            }
            catch (Exception e) when (e is ArgumentException || e is EncoderFallbackException || e is FormatException || e is ObjectDisposedException)
            {
                result = null;
                return false;
            }
        }
    }
}