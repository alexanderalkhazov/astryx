using System.Text.Json;
using Astryx.SharedKernel.Json;

namespace Astryx.Infrastructure;

/// <summary>Serializer settings for persisted payloads (outbox messages, job payloads): web casing, decimals as strings.</summary>
internal static class InfrastructureJson
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddFinancialConverters();
}
