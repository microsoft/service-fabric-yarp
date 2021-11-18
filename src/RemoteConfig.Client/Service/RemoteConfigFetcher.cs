// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.RemoteConfig.Contract;
using Yarp.ServiceFabric.RemoteConfig.Infra;

namespace Yarp.ServiceFabric.RemoteConfig
{
    /// <summary>
    /// Provides a method to fetch real-time config updates from a destination service.
    /// </summary>
    internal class RemoteConfigFetcher : IRemoteConfigFetcher, IDisposable
    {
        private static readonly int PollTimeInSeconds = 10;

        private readonly IRemoteConfigEndpointResolver endpointResolver;
        private readonly ILogger<RemoteConfigFetcher> logger;
        private readonly IOperationLogger operationLogger;
        private readonly JsonSerializerOptions jsonOptions;
        private readonly HttpMessageInvoker httpClient;

        private bool isDisposed;

        public RemoteConfigFetcher(
            IRemoteConfigEndpointResolver endpointResolver,
            HttpMessageInvoker httpClient,
            ILogger<RemoteConfigFetcher> logger,
            IOperationLogger operationLogger)
        {
            this.endpointResolver = endpointResolver ?? throw new ArgumentNullException(nameof(endpointResolver));
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.operationLogger = operationLogger ?? throw new ArgumentNullException(nameof(operationLogger));

            this.jsonOptions = new JsonSerializerOptions()
                .ApplyIslandGatewayRemoteConfigSettings();
        }

        private enum Outcome
        {
            Success,
            NotModified,
            Timeout,
            DeliberatelyCanceled,
            StarvedSockets,
            DestinationServerError,
            UnhandledException,
        }

        /// <summary>
        /// Gets a stream of real-time updates from a remote config provider.
        /// The async enumerable completes gracefully when no further updated can be fetched from the current destination,
        /// and when that happens retrying is advised, which will again use <see cref="IRemoteConfigEndpointResolver"/>
        /// to determine the correct endpoint to call.
        /// </summary>
        /// <remarks>
        /// This method never throws under normal (inclluding planned failure) situations.
        /// Instead, the stream runs to completion, and the caller is supposed to retry.
        /// </remarks>
        public async IAsyncEnumerable<(RemoteConfigResponseDto Config, string ETag)> GetConfigurationStream(string lastSeenETag, [EnumeratorCancellation] CancellationToken cancellation)
        {
            if (this.isDisposed)
            {
                throw new ObjectDisposedException(nameof(RemoteConfigFetcher));
            }

            var uri = await this.endpointResolver.TryGetNextEndpoint(cancellation);
            if (uri == null)
            {
                yield break;
            }

            while (true)
            {
                var (outcome, result) = await this.operationLogger.ExecuteAsync(
                    "RemoteConfigClient.ExecuteRequest",
                    async () =>
                    {
                        this.operationLogger.Context.SetProperty("requestUri", uri.ToString());
                        this.operationLogger.Context.SetProperty("pollTimeout", PollTimeInSeconds.ToString());
                        this.operationLogger.Context.SetProperty("oldEtag", lastSeenETag ?? string.Empty);

                        var (outcome, result, error, exception) = await this.ExecuteRequestAsync(uri, lastSeenETag, cancellation);

                        this.operationLogger.Context.SetProperty("outcome", outcome.ToString());
                        if (error != null)
                        {
                            this.operationLogger.Context.SetProperty("error", error.ToString());
                        }
                        if (exception != null)
                        {
                            this.operationLogger.Context.SetProperty("excType", exception.GetType().FullName);
                            this.operationLogger.Context.SetProperty("excMsg", exception.Message);
                        }
                        if (result != null)
                        {
                            this.operationLogger.Context.SetProperty("newEtag", result.Value.ETag ?? string.Empty);
                        }

                        return (outcome, result);
                    });

                if (outcome == Outcome.Success)
                {
                    lastSeenETag = result.Value.ETag;
                    yield return result.Value;
                }
                else if (outcome == Outcome.NotModified)
                {
                    // All good, just nothing changed yet. Keep polling on the next iteration...
                }
                else if (outcome == Outcome.DeliberatelyCanceled)
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Execution should never get here.");
                }
                else
                {
                    // In any other case, bail out and allow the caller to try a different server.
                    yield break;
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (this.isDisposed)
            {
                return;
            }

            this.httpClient.Dispose();
            this.isDisposed = true;
        }

        private async Task<(Outcome Outcome, (RemoteConfigResponseDto Config, string ETag)? Result, string ErrorMessage, Exception Exception)> ExecuteRequestAsync(Uri uri, string lastSeenETag, CancellationToken cancellation)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(RemoteConfigConsts.PollTimeoutHeader, PollTimeInSeconds.ToString());
            if (!string.IsNullOrEmpty(lastSeenETag))
            {
                request.Headers.Add(RemoteConfigConsts.IfNoneMatchHeader, lastSeenETag);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            cts.CancelAfter(TimeSpan.FromSeconds(PollTimeInSeconds * 2));
            try
            {
                using var response = await this.httpClient.SendAsync(request, cts.Token);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string etag = null;
                    if (response.Headers.TryGetValues(RemoteConfigConsts.ETagHeader, out var etagValues))
                    {
                        etag = etagValues.FirstOrDefault();
                    }

                    using var stream = await response.Content.ReadAsStreamAsync();
                    var config = await JsonSerializer.DeserializeAsync<RemoteConfigResponseDto>(stream, this.jsonOptions, cts.Token);
                    return (Outcome.Success, (config, etag), null, null);
                }
                else if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return (Outcome.NotModified, null, null, null);
                }

                return (Outcome.DestinationServerError, null, $"Received non-success status code {response.StatusCode}.", null);
            }
            catch (HttpRequestException ex)
            {
                if (ex.InnerException is SocketException socketException)
                {
                    // See: https://msazure.visualstudio.com/One/_git/CoreFramework?path=%2Fsrc%2FCoreFramework%2FCoreFramework.Communication.Http%2FExceptions%2FHttpCommunicationException.cs&version=GBmaster&line=100&lineEnd=106&lineStartColumn=1&lineEndColumn=1&lineStyle=plain&_a=contents
                    var socketError = socketException.SocketErrorCode;
                    if (socketError == SocketError.NoBufferSpaceAvailable ||
                        socketError == SocketError.TooManyOpenSockets)
                    {
                        return (Outcome.StarvedSockets, null, $"Starved sockets: {ex.Message}", ex);
                    }
                }

                return (Outcome.DestinationServerError, null, $"Transport error: {ex.Message}", ex);
            }
            catch (OperationCanceledException)
            {
                if (cts.IsCancellationRequested && !cancellation.IsCancellationRequested)
                {
                    return (Outcome.Timeout, null, null, null);
                }

                return (Outcome.DeliberatelyCanceled, null, null, null);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, $"Unhandled exception while calling remote config endpoint '{request.RequestUri}'");
                return (Outcome.UnhandledException, null, "Unhandled exception.", ex);
            }
        }
    }
}
