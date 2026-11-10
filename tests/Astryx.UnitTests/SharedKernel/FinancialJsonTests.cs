using System.Text.Json;
using Astryx.SharedKernel.Finance;
using Astryx.SharedKernel.Json;

namespace Astryx.UnitTests.SharedKernel;

public class FinancialJsonTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddFinancialConverters();

    private sealed record Dto(decimal Amount, Price Entry, Quantity Quantity, Percent RiskPerTradePct);

    [Fact]
    public void Round_trips_as_strings_preserving_scale()
    {
        var dto = new Dto(14_174.00m, new Price(186.50m), new Quantity(76m), new Percent(0.0075m));

        var json = JsonSerializer.Serialize(dto, Options);

        json.ShouldBe("""{"amount":"14174.00","entry":"186.50","quantity":"76","riskPerTradePct":"0.0075"}""");
        JsonSerializer.Deserialize<Dto>(json, Options).ShouldBe(dto);
    }

    [Theory]
    [InlineData("""{"amount":14174.00,"entry":"186.50","quantity":"76","riskPerTradePct":"0.0075"}""")] // number, not string
    [InlineData("""{"amount":"1e4","entry":"186.50","quantity":"76","riskPerTradePct":"0.0075"}""")]    // exponent
    [InlineData("""{"amount":"12,5","entry":"186.50","quantity":"76","riskPerTradePct":"0.0075"}""")]   // culture separator
    [InlineData("""{"amount":"1.00","entry":"0","quantity":"76","riskPerTradePct":"0.0075"}""")]       // zero price
    [InlineData("""{"amount":"1.00","entry":"186.50","quantity":"-1","riskPerTradePct":"0.0075"}""")]  // negative quantity
    public void Rejects_non_string_or_invalid_values(string json) =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Dto>(json, Options));
}
