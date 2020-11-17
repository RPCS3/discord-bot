using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CompatApiClient
{
    public sealed class CompatApiCommitHashConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number
                && !reader.HasValueSequence
                && reader.ValueSpan.Length == 1
                && reader.ValueSpan[0] == (byte)'0')
            {
                _ = reader.GetInt32();
                return null;
            }

            return reader.GetString();
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }
}