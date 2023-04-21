using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CompatApiClient;

public sealed class CompatApiCommitHashConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader is not { TokenType: JsonTokenType.Number, HasValueSequence: false, ValueSpan: [(byte)'0'] })
            return reader.GetString();
        
        _ = reader.GetInt32();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}