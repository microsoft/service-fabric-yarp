// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IslandGateway.Common;
using IslandGateway.FabricDiscovery.IslandGatewayConfig;
using IslandGateway.FabricDiscovery.Util;
using IslandGateway.RemoteConfig.Contract;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IslandGateway.FabricDiscovery.Controllers
{
    /// <summary>
    /// Serves Island Gateway configuration for consumption by IGW instances.
    /// </summary>
    [ApiController]
    [Route("/api/v1/yarpconfig")]
    public class YarpConfigController : ControllerBase
    {
        private const int MinPollTimeoutSeconds = 1;
        private const int MaxPollTimeoutSeconds = 30;
        private const int DefaultPollTimeoutSeconds = 10;

        private readonly DIAdapter diAdapter;

        /// <summary>
        /// Initializes a new instance of the <see cref="YarpConfigController"/> class.
        /// </summary>
        public YarpConfigController(DIAdapter diAdapter)
        {
            this.diAdapter = diAdapter ?? throw new ArgumentNullException(nameof(diAdapter));
        }

        /// <summary>
        /// Provides the current configuration snapshot,
        /// with optional support for long-polling allowing for real-time updates.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetYarpConfig()
        {
            var igwConfigProvider = this.diAdapter.GetService<ISnapshotProvider<IslandGatewaySerializedConfig>>();
            var snapshot = igwConfigProvider?.GetSnapshot();
            if (snapshot == null)
            {
                return this.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            string ifNoneMatch = this.GetHeaderOrNull(RemoteConfigConsts.IfNoneMatchHeader);
            if (!string.IsNullOrEmpty(ifNoneMatch) && snapshot.Value.ETag == ifNoneMatch)
            {
                // Wait for the next change
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(this.HttpContext.RequestAborted);
                cts.CancelAfter(this.GetPollTimeoutSeconds());

                try
                {
                    await snapshot.ChangeToken.WaitForChanges(cts.Token);
                    snapshot = igwConfigProvider.GetSnapshot();
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !this.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    return this.StatusCode(StatusCodes.Status304NotModified);
                }
            }

            var payload = snapshot.Value;

            this.Response.StatusCode = StatusCodes.Status200OK;
            this.Response.Headers.Add(RemoteConfigConsts.ETagHeader, payload.ETag);
            this.Response.ContentType = payload.ContentType;
            this.Response.ContentLength = payload.Bytes.Length;
            if (!string.IsNullOrEmpty(payload.ContentEncoding))
            {
                this.Response.Headers.Add("Content-Encoding", payload.ContentEncoding);
            }

            await this.Response.Body.WriteAsync(payload.Bytes, 0, payload.Bytes.Length);
            return new EmptyResult();
        }

        private TimeSpan GetPollTimeoutSeconds()
        {
            string pollTimeoutSeconds = this.GetHeaderOrNull(RemoteConfigConsts.PollTimeoutHeader);
            if (!string.IsNullOrEmpty(pollTimeoutSeconds))
            {
                if (int.TryParse(pollTimeoutSeconds, out var parsed) &&
                    parsed >= MinPollTimeoutSeconds &&
                    parsed <= MaxPollTimeoutSeconds)
                {
                    return TimeSpan.FromSeconds(parsed);
                }
            }

            return TimeSpan.FromSeconds(DefaultPollTimeoutSeconds);
        }

        private string GetHeaderOrNull(string headerName)
        {
            string value = null;
            if (this.Request.Headers.TryGetValue(headerName, out var values))
            {
                value = values.FirstOrDefault();
            }

            return value;
        }
    }
}
