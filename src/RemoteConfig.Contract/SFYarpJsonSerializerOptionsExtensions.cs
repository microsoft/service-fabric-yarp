// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Yarp.ServiceFabric.RemoteConfig.Contract
{
    /// <summary>
    /// Provides  a method to configure serialization/deserialization settings for SFYarp remote config objects.
    /// </summary>
    public static class SFYarpJsonSerializerOptionsExtensions
    {
        /// <summary>
        /// Configured a <see cref="JsonSerializerOptions"/> with converters needed for serializing/deserializing
        /// SFYarp remote config objects (<see cref="RemoteConfigResponseDto"/>).
        /// </summary>
        public static JsonSerializerOptions ApplySFYarpRemoteConfigSettings(this JsonSerializerOptions options)
        {
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PropertyNameCaseInsensitive = true;
            options.Converters.Add(new TimeSpanConverter());

            return options;
        }
    }
}
