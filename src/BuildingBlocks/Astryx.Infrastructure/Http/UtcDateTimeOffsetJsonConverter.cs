using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astryx.Infrastructure.Http;

/// <summary>
/// Writes every instant in UTC with a <c>Z</c> suffix (<c>2026-11-09T19:32:00Z</c>), as SYSTEM_DESIGN.md §20.3
/// requires; reads any ISO 8601 offset and normalises it to UTC.
/// </summary>
public sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
}
