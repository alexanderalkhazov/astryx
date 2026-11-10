using System.Text.Json;
using Astryx.Infrastructure.Audit;

namespace Astryx.UnitTests.Audit;

public class AuditRedactorTests
{
    private static JsonElement Redact(object value) => JsonDocument.Parse(AuditRedactor.ToRedactedJson(value)!).RootElement;

    [Fact]
    public void Secrets_are_redacted_in_any_casing_or_separator_style()
    {
        var json = Redact(new Dictionary<string, object?>
        {
            ["password"] = "hunter2hunter2",
            ["RefreshToken"] = "abc",
            ["refresh_token"] = "abc",
            ["x-api-key"] = "k",
            ["userKey"] = "u",
            ["credentials_ciphertext"] = "c",
            ["displayName"] = "Alex",
        });

        foreach (var key in new[] { "password", "RefreshToken", "refresh_token", "x-api-key", "userKey", "credentials_ciphertext" })
        {
            json.GetProperty(key).GetString().ShouldBe(AuditRedactor.Redacted);
        }

        json.GetProperty("displayName").GetString().ShouldBe("Alex");
    }

    [Fact]
    public void Nested_objects_and_arrays_are_redacted()
    {
        var json = Redact(new
        {
            connection = new { broker = "etoro", apiKey = "pk_live", environment = "demo" },
            sessions = new[] { new { deviceId = "ios-1", token = "t1" }, new { deviceId = "ios-2", token = "t2" } },
        });

        json.GetProperty("connection").GetProperty("apiKey").GetString().ShouldBe(AuditRedactor.Redacted);
        json.GetProperty("connection").GetProperty("environment").GetString().ShouldBe("demo");
        json.GetProperty("sessions")[1].GetProperty("token").GetString().ShouldBe(AuditRedactor.Redacted);
        json.GetProperty("sessions")[1].GetProperty("deviceId").GetString().ShouldBe("ios-2");
    }

    [Theory]
    [InlineData("trader@example.com", "t***@example.com")]
    [InlineData("a@b.io", "a***@b.io")]
    [InlineData("not-an-email", "[REDACTED]")]
    public void Emails_are_masked(string email, string expected) =>
        Redact(new { email }).GetProperty("email").GetString().ShouldBe(expected);

    [Fact]
    public void Money_stays_a_decimal_string() =>
        Redact(new { maxRiskPerTradePct = 0.0075m }).GetProperty("maxRiskPerTradePct").GetString().ShouldBe("0.0075");

    [Fact]
    public void Oversized_payloads_are_replaced_by_a_marker()
    {
        var json = Redact(new { notes = new string('x', 20_000) });

        json.GetProperty("truncated").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Null_stays_null() => AuditRedactor.ToRedactedJson(null).ShouldBeNull();
}
