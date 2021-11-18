// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Yarp.ServiceFabric.FabricDiscovery.Util.Tests
{
    public class AsyncEnumerableExtensionsTests
    {
        [Fact]
        public async Task FirstOrDefaultAsync_Works()
        {
            var result = await SecondItemNeverComes().FirstOrDefaultAsync();
            result.Should().Be("good");
        }

        [Fact]
        public async Task FirstOrDefaultAsync_Cancellation()
        {
            // Arrange
            using var cts = new CancellationTokenSource();

            // act
            var task = FirstItemNeverComes().FirstOrDefaultAsync(cts.Token);
            cts.Cancel();
            Func<Task> func = async () => await task;

            // Assert
            await func.Should().ThrowAsync<OperationCanceledException>();
        }

        private static async IAsyncEnumerable<string> SecondItemNeverComes([EnumeratorCancellation] CancellationToken cancellation = default)
        {
            yield return "good";
            await Task.Delay(-1, cancellation);

            // Execution will never get here
            yield return "bad";
        }

        private static async IAsyncEnumerable<string> FirstItemNeverComes([EnumeratorCancellation] CancellationToken cancellation = default)
        {
            await Task.Delay(-1, cancellation);

            // Execution will never get here
            yield return "bad";
        }
    }
}