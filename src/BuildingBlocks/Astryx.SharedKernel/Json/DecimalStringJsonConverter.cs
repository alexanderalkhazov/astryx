using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Astryx.SharedKernel.Json;

/// <summary>
/// Serialises decimals as JSON strings and accepts only strings when reading, so financial values never pass
/// through JavaScript numbers (SYSTEM_DESIGN.md §11.1, ADR-003). Scale is preserved: 186.50m ↔ "186.50".
/// </summary>
public sealed partial class DecimalStringJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Financial values must be JSON strings, e.g. \"186.50\".");
        }

        var text = reader.GetString()!;
        if (!DecimalPattern().IsMatch(text) || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            throw new JsonException($"'{text}' is not a decimal string.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));

    [GeneratedRegex(@"^-?\d{1,20}(\.\d{1,10})?$")]
    private static partial Regex DecimalPattern();
}
