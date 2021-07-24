// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace IslandGateway.RemoteConfig.Contract
{
    /// <summary>
    /// Serializes and deserializes TimeSpan's in a sane manner, contrary to the default with System.Text.Json.
    /// See <see href="https://github.com/dotnet/runtime/issues/29932"/>.
    /// </summary>
    internal class TimeSpanConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return XmlConvert.ToTimeSpan(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(XmlConvert.ToString(value));
        }
    }
}