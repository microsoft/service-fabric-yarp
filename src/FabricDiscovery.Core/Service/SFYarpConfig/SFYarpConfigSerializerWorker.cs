// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ServiceFabric.Common;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.FabricDiscovery.Util;
using Yarp.ServiceFabric.RemoteConfig.Contract;

namespace Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig
{
    /// <summary>
    /// Manages discovery of Service Fabric Properties for services that have opted-in
    /// via label <c>Yarp.EnableDynamicOverrides</c>, and computes the final Island Gateway configs.
    /// The processing sequence is
    ///   * <see cref="Topology.TopologyDiscoveryWorker"/>, then
    ///   * <see cref="SFYarpTopologyMapperWorker"/>, then
    ///   * <see cref="SFYarpConfigProducerWorker"/>, then
    ///   * this.
    /// </summary>
    internal class SFYarpConfigSerializerWorker : CriticalBackgroundService, ISnapshotProvider<SFYarpSerializedConfig>
    {
        private readonly IProxyConfigProvider igwConfigProvider;
        private readonly ILogger<SFYarpConfigSerializerWorker> logger;
        private readonly IOperationLogger operationLogger;
        private readonly RecurringTask recurringTask;
        private readonly JsonSerializerOptions jsonOptions;
        private readonly SHA256 sha256;

        private IProxyConfig currentConfigSnapshot;

        private Snapshot<SFYarpSerializedConfig> snapshot;
        private CancellationTokenSource changeToken;

        public SFYarpConfigSerializerWorker(
            IProxyConfigProvider igwConfigProvider,
            IProcessExiter processExiter,
            ILogger<SFYarpConfigSerializerWorker> logger,
            IOperationLogger operationLogger)
            : base(new WorkerOptions(), processExiter, logger)
        {
            this.igwConfigProvider = igwConfigProvider ?? throw new ArgumentNullException(nameof(igwConfigProvider));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));
            this.recurringTask = new RecurringTask(this.RunIterationAsync)
                .WithLogging(logger, operationLogger, "SFYarpConfigSerializerWorker.Iteration")
                .WithAbortOnFailure(); // This worker should never fail, a single failure is grounds to abort

            this.jsonOptions = new JsonSerializerOptions()
                .ApplySFYarpRemoteConfigSettings();
            this.sha256 = SHA256.Create();
        }

        public Snapshot<SFYarpSerializedConfig> GetSnapshot() => this.snapshot;

        protected override Task InitAsync(CancellationToken cancellation)
        {
            this.currentConfigSnapshot = this.igwConfigProvider.GetConfig();
            if (this.currentConfigSnapshot == null)
            {
                throw new InvalidOperationException($"Island Gateway config should already be known when {nameof(SFYarpConfigSerializerWorker)} starts.");
            }

            this.UpdateSnapshot();
            return Task.CompletedTask;
        }

        protected override Task RunAsync(CancellationToken cancellation) => this.recurringTask.RunAsync(cancellation);

        /// <summary>
        /// Executes one iteration which involves scanning through all services, fetching properties for those that are due,
        /// and computing the YARP model for each service.
        /// </summary>
        private async Task RunIterationAsync(CancellationToken cancellation)
        {
            await this.currentConfigSnapshot.ChangeToken.WaitForChanges(cancellation);
            this.currentConfigSnapshot = this.igwConfigProvider.GetConfig();
            this.UpdateSnapshot();
        }

        private void UpdateSnapshot()
        {
            var config = this.currentConfigSnapshot;
            this.logger.LogInformation($"Serializing new Island Gateway configs. {config.Clusters.Count} clusters, {config.Routes.Count} routes.");

            using var oldToken = this.changeToken;
            this.changeToken = new CancellationTokenSource();

            var serialized = this.SerializeConfig(config);
            this.snapshot = new Snapshot<SFYarpSerializedConfig>(serialized, new CancellationChangeToken(this.changeToken.Token));

            try
            {
                oldToken?.Cancel(throwOnFirstException: false);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error signaling change.");
            }
        }

        private SFYarpSerializedConfig SerializeConfig(IProxyConfig config)
        {
            var dto = new RemoteConfigResponseDto
            {
                Clusters = config.Clusters,
                Routes = config.Routes,
                AsOf = DateTimeOffset.UtcNow,
            };

            var bytes = this.operationLogger.Execute(
                "FabricDiscovery.SerializeConfig",
                () =>
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
                        using (var writeLengthStream = new WriteLengthStream(gzipStream))
                        {
                            using (var jsonStream = new Utf8JsonWriter(writeLengthStream))
                            {
                                JsonSerializer.Serialize(jsonStream, dto, this.jsonOptions);
                            }

                            this.operationLogger.Context.SetProperty("uncompressedBytes", writeLengthStream.BytesWritten.ToString());
                        }

                        this.operationLogger.Context.SetProperty("compressedBytes", memoryStream.Length.ToString());
                        return memoryStream.ToArray();
                    }
                });

            var etagBytes = this.sha256.ComputeHash(bytes);
            var etag = BitConverter.ToString(etagBytes).Replace("-", string.Empty).ToLowerInvariant();

            return new SFYarpSerializedConfig(
                bytes: bytes,
                etag: $"\"{etag}\"",
                contentType: "application/json",
                contentEncoding: "gzip");
        }
    }
}
