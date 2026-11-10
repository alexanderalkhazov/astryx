using System.Text.Json;
using System.Text.Json.Serialization;
using Astryx.SharedKernel.Finance;

namespace Astryx.SharedKernel.Json;

public sealed class PriceJsonConverter : JsonConverter<Price>
{
    private static readonly DecimalStringJsonConverter Inner = new();

    public override Price Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = Inner.Read(ref reader, typeof(decimal), options);
        return Price.TryCreate(value, out var price) ? price : throw new JsonException("Price must be greater than zero.");
    }

    public override void Write(Utf8JsonWriter writer, Price value, JsonSerializerOptions options) => Inner.Write(writer, value.Value, options);
}

public sealed class QuantityJsonConverter : JsonConverter<Quantity>
{
    private static readonly DecimalStringJsonConverter Inner = new();

    public override Quantity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = Inner.Read(ref reader, typeof(decimal), options);
        return value >= 0m ? new Quantity(value) : throw new JsonException("Quantity cannot be negative.");
    }

    public override void Write(Utf8JsonWriter writer, Quantity value, JsonSerializerOptions options) => Inner.Write(writer, value.Value, options);
}

public sealed class PercentJsonConverter : JsonConverter<Percent>
{
    private static readonly DecimalStringJsonConverter Inner = new();

    public override Percent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(Inner.Read(ref reader, typeof(decimal), options));

    public override void Write(Utf8JsonWriter writer, Percent value, JsonSerializerOptions options) => Inner.Write(writer, value.Fraction, options);
}

public static class FinancialJson
{
    /// <summary>Registers every financial converter; the API host calls this on its JSON options.</summary>
    public static JsonSerializerOptions AddFinancialConverters(this JsonSerializerOptions options)
    {
        options.Converters.Add(new DecimalStringJsonConverter());
        options.Converters.Add(new PriceJsonConverter());
        options.Converters.Add(new QuantityJsonConverter());
        options.Converters.Add(new PercentJsonConverter());
        return options;
    }
}
