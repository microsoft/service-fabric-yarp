// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;

namespace IslandGateway.FabricDiscovery.Util
{
    /// <summary>
    /// Represents a result as either
    /// successful (with a corresponding <see cref="Value"/> of type <typeparamref name="TSuccess"/>)
    /// or failure (with a corresponding <see cref="Error"/> of type <typeparamref name="TError"/>).
    /// </summary>
    /// <typeparam name="TSuccess">Successful result type.</typeparam>
    /// <typeparam name="TError">Failure result type.</typeparam>
    internal readonly struct Result<TSuccess, TError>
    {
        private readonly TSuccess value;
        private readonly TError error;

        internal Result(bool isSuccess, TSuccess value, TError error)
        {
            this.IsSuccess = isSuccess;
            this.value = value;
            this.error = error;
        }

        public bool IsSuccess { get; }

        public TSuccess Value
        {
            get
            {
                if (!this.IsSuccess)
                {
                    throw new Exception($"Cannot get {nameof(this.Value)} of a failure result.");
                }

                return this.value;
            }
        }

        public TError Error
        {
            get
            {
                if (this.IsSuccess)
                {
                    throw new Exception($"Cannot get {nameof(this.Error)} of a successful result.");
                }

                return this.error;
            }
        }

        public static Result<TSuccess, TError> Success(TSuccess value)
        {
            return new Result<TSuccess, TError>(true, value, default);
        }

        public static Result<TSuccess, TError> Failure(TError error)
        {
            return new Result<TSuccess, TError>(false, default, error);
        }
    }

    internal static class Result
    {
        /// <summary>
        /// Combines the error results, if any, of the provided arguments.
        /// The result is similar to <see cref="string.Join(string?, object?[])"/>
        /// where each error result is converted to string with the default <see cref="object.ToString"/>.
        /// </summary>
        public static string JoinErrors<TSuccess1, TError1, TSuccess2, TError2>(string separator, Result<TSuccess1, TError1> result1, Result<TSuccess2, TError2> result2)
        {
            var builder = new StringBuilder();
            if (!result1.IsSuccess)
            {
                builder.Append(result1.Error);
            }

            if (!result2.IsSuccess)
            {
                if (builder.Length > 0)
                {
                    builder.Append(separator);
                }
                builder.Append(result2.Error);
            }

            return builder.ToString();
        }
    }
}
