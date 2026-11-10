using System.Text.Json;
using Astryx.SharedKernel.Json;

namespace Astryx.Infrastructure.Outbox;

internal static class OutboxSerializer
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web).AddFinancialConverters();
}
