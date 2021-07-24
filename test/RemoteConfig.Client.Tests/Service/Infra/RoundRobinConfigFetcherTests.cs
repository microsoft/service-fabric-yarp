// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using IslandGateway.RemoteConfig.Contract;
using Xunit;

namespace IslandGateway.RemoteConfig.Infra.Tests
{
    public class RoundRobinConfigFetcherTests
    {
        [Fact]
        public async Task Basics_Work()
        {
            // Arrange
            var response1 = (new RemoteConfigResponseDto(), "\"etag1\"");
            var fetcher1 = new DummyFetcher(new[] { response1 });

            var response2 = (new RemoteConfigResponseDto(), "\"etag1\"");
            var fetcher2 = new DummyFetcher(new[] { response2 });

            var sut = new RoundRobinConfigFetcher(new[] { fetcher1, fetcher2 });

            // Act & Assert
            (await FirstOrDefaultAsync(sut.GetConfigurationStream("\"etag1\"", CancellationToken.None))).Should().Be(response1);
            fetcher1.LastProvdedEtag.Should().Be("\"etag1\"");

            (await FirstOrDefaultAsync(sut.GetConfigurationStream("\"etag2\"", CancellationToken.None))).Should().Be(response2);
            fetcher2.LastProvdedEtag.Should().Be("\"etag2\"");

            (await FirstOrDefaultAsync(sut.GetConfigurationStream("\"etag3\"", CancellationToken.None))).Should().Be(response1);
            fetcher1.LastProvdedEtag.Should().Be("\"etag3\"");

            (await FirstOrDefaultAsync(sut.GetConfigurationStream("\"etag4\"", CancellationToken.None))).Should().Be(response2);
            fetcher2.LastProvdedEtag.Should().Be("\"etag4\"");
        }

        private static async Task<T> FirstOrDefaultAsync<T>(IAsyncEnumerable<T> enumerable)
        {
            await foreach (var item in enumerable)
            {
                return item;
            }

            return default;
        }

        private class DummyFetcher : IRemoteConfigFetcher
        {
            private readonly IEnumerable<(RemoteConfigResponseDto Config, string ETag)> values;

            public DummyFetcher(IEnumerable<(RemoteConfigResponseDto Config, string ETag)> values)
            {
                this.values = values ?? throw new System.ArgumentNullException(nameof(values));
            }

            public string LastProvdedEtag { get; set; }

            public async IAsyncEnumerable<(RemoteConfigResponseDto Config, string ETag)> GetConfigurationStream(string lastSeenETag, [EnumeratorCancellation] CancellationToken cancellation)
            {
                this.LastProvdedEtag = lastSeenETag;
                foreach (var value in this.values)
                {
                    await Task.Yield();
                    yield return value;
                }
            }
        }
    }
}
