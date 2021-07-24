// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace IslandGateway.RemoteConfig.Contract
{
    /// <summary>
    /// Provides  a method to configure serialization/deserialization settings for Island Gateway remote config objects.
    /// </summary>
    public static class IslandGatewayJsonSerializerOptionsExtensions
    {
        /// <summary>
        /// Configured a <see cref="JsonSerializerOptions"/> with converters needed for serializing/deserializing
        /// Island Gateway remote config objects (<see cref="RemoteConfigResponseDto"/>).
        /// </summary>
        public static JsonSerializerOptions ApplyIslandGatewayRemoteConfigSettings(this JsonSerializerOptions options)
        {
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PropertyNameCaseInsensitive = true;
            options.Converters.Add(new TimeSpanConverter());

            return options;
        }
    }
}
