// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using IslandGateway.Common.Telemetry;
using IslandGateway.RemoteConfig.Contract;
using IslandGateway.RemoteConfig.Infra;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace IslandGateway.RemoteConfig.Tests
{
    public class RemoteConfigFetcherTests
    {
        private const string TestEndpoint = "https://localhost:1234/dummyEndpoint";

        [Fact]
        public async Task Basics_Work()
        {
            int i = 0;
            var asyncEnumerable = Go(
                httpSimulationFunc: async (request, cancellation) =>
                {
                    await Task.Yield();
                    request.RequestUri.Should().Be(TestEndpoint);

                    switch (i++)
                    {
                        case 0:
                            {
                                // Produce first response, with etag0
                                request.Headers.Contains(RemoteConfigConsts.IfNoneMatchHeader).Should().BeFalse();
                                request.Headers.GetValues(RemoteConfigConsts.PollTimeoutHeader).SingleOrDefault().Should().Be("10");
                                var response = new HttpResponseMessage(HttpStatusCode.OK);
                                response.Content = new StringContent(@"{""clusters"":[],""routes"":[]}", Encoding.UTF8, "application/json");
                                response.Headers.Add(RemoteConfigConsts.ETagHeader, "\"etag0\"");
                                return response;
                            }
                        case 1:
                            {
                                // Produce a NotModified response
                                request.Headers.GetValues(RemoteConfigConsts.IfNoneMatchHeader).SingleOrDefault().Should().Be("\"etag0\"");
                                request.Headers.GetValues(RemoteConfigConsts.PollTimeoutHeader).SingleOrDefault().Should().Be("10");
                                var response = new HttpResponseMessage(HttpStatusCode.NotModified);
                                return response;
                            }
                        case 2:
                            {
                                // Produce new response with etag1
                                request.Headers.GetValues(RemoteConfigConsts.IfNoneMatchHeader).SingleOrDefault().Should().Be("\"etag0\"");
                                request.Headers.GetValues(RemoteConfigConsts.PollTimeoutHeader).SingleOrDefault().Should().Be("10");
                                var response = new HttpResponseMessage(HttpStatusCode.OK);
                                response.Content = new StringContent(@"{""clusters"":[{""id"":""cluster1""},{""id"":""cluster2""}],""routes"":[{""id"": ""route1""}]}", Encoding.UTF8, "application/json");
                                response.Headers.Add(RemoteConfigConsts.ETagHeader, "\"etag1\"");
                                return response;
                            }
                    }

                    throw new HttpRequestException();
                },
                lastSeenEtag: null,
                cancellation: CancellationToken.None);

            int receivedConfigs = 0;
            await foreach (var item in asyncEnumerable)
            {
                switch (receivedConfigs++)
                {
                    case 0:
                        item.ETag.Should().Be("\"etag0\"");
                        item.Config.Clusters.Should().BeEmpty();
                        item.Config.Routes.Should().BeEmpty();
                        break;
                    case 1:
                        item.ETag.Should().Be("\"etag1\"");
                        item.Config.Clusters.Should().HaveCount(2);
                        item.Config.Routes.Should().HaveCount(1);
                        break;
                    default:
                        throw new InvalidOperationException("Received an extra config than was expected!");
                }
            }

            receivedConfigs.Should().Be(2);
        }

        private static IAsyncEnumerable<(RemoteConfigResponseDto Config, string ETag)> Go(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> httpSimulationFunc, string lastSeenEtag, CancellationToken cancellation)
        {
            var endpointResolverMock = new Mock<IRemoteConfigEndpointResolver>();
            endpointResolverMock
                .Setup(e => e.TryGetNextEndpoint(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Uri(TestEndpoint));

            var httpClient = MockHttpHandler.CreateClient(httpSimulationFunc);

            var sut = new RemoteConfigFetcher(endpointResolverMock.Object, httpClient, new Mock<ILogger<RemoteConfigFetcher>>().Object, new NullOperationLogger());
            return sut.GetConfigurationStream(lastSeenEtag, cancellation);
        }

        private class MockHttpHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> func;

            private MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> func)
            {
                this.func = func ?? throw new ArgumentNullException(nameof(func));
            }

            public static HttpMessageInvoker CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> func)
            {
                var handler = new MockHttpHandler(func);
                return new HttpMessageInvoker(handler);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return this.func(request, cancellationToken);
            }
        }
    }
}
