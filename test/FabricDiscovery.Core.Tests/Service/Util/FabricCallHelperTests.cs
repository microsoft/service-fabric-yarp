// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Util.Tests
{
    public class FabricCallHelperTests
    {
        [Fact]
        public async Task RunWithRetries_FirstAttemptSucceeds()
        {
            // Arrange
            var attempts = new List<int>();

            // Act
            var result = await FabricCallHelper.RunWithExponentialRetries(
                async (attempt, _) =>
                {
                    attempts.Add(attempt);

                    await Task.Yield();
                    return "ok";
                },
                FabricExponentialRetryPolicy.Default,
                CancellationToken.None);

            // Assert
            attempts.Should().Equal(new[] { 1 });
            result.Should().Be("ok");
        }

        [Fact]
        public async Task RetryTransientFailures_RetriesTransientErrors()
        {
            // Arrange
            var attempts = new List<int>();

            // Act
            var result = await FabricCallHelper.RunWithExponentialRetries(
                async (attempt, _) =>
                {
                    attempts.Add(attempt);

                    await Task.Yield();
                    if (attempt == 1)
                    {
                        throw new TimeoutException("transient");
                    }
                    else if (attempt == 2)
                    {
                        throw new FabricTransientException();
                    }

                    return "ok";
                },
                FabricExponentialRetryPolicy.Default,
                CancellationToken.None);

            // Assert
            attempts.Should().Equal(new[] { 1, 2, 3 });
            result.Should().Be("ok");
        }

        [Fact]
        public async Task RetryTransientFailures_DoesNotRetryNonTransient()
        {
            // Arrange
            var attempts = new List<int>();

            // Act
            Func<Task> func = () => FabricCallHelper.RunWithExponentialRetries<string>(
                async (attempt, _) =>
                {
                    attempts.Add(attempt);

                    await Task.Yield();
                    throw new Exception("boom");
                },
                FabricExponentialRetryPolicy.Default,
                CancellationToken.None);

            // Assert
            await func.Should().ThrowAsync<Exception>().WithMessage("boom");
            attempts.Should().Equal(new[] { 1 });
        }

        [Fact]
        public async Task RetryTransientFailures_TranslatesDeliberateCancellations()
        {
            // Arrange
            var attempts = new List<int>();
            using var cts = new CancellationTokenSource();
            var tcs1 = new TaskCompletionSource<int>();
            var tcs2 = new TaskCompletionSource<int>();

            // Act
            var task = FabricCallHelper.RunWithExponentialRetries<string>(
                async (attempt, _) =>
                {
                    attempts.Add(attempt);

                    await Task.Yield();
                    tcs1.SetResult(0);

                    await tcs2.Task;
                    throw new FabricTransientException(FabricErrorCode.OperationCanceled);
                },
                FabricExponentialRetryPolicy.Default,
                cts.Token);

            await tcs1.Task;

            cts.Cancel();
            tcs2.SetResult(0);

            Func<Task> func = () => task;

            // Assert
            await func.Should().ThrowAsync<OperationCanceledException>();
            attempts.Should().Equal(new[] { 1 });
        }

        [Fact]
        public async Task RunWithRetries_AbortsWhenCanceled()
        {
            // Arrange
            var attempts = new List<int>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            Func<Task> func = () => FabricCallHelper.RunWithExponentialRetries<string>(
                (attempt, _) =>
                {
                    attempts.Add(attempt);
                    throw new Exception("Execvution should never get here.");
                },
                FabricExponentialRetryPolicy.Default,
                cts.Token);

            // Assert
            await func.Should().ThrowAsync<OperationCanceledException>();
            attempts.Should().BeEmpty();
        }
    }
}
