using Microsoft.Extensions.Configuration;

namespace Astryx.Infrastructure;

/// <summary>Fail-closed checks run before the host starts (SYSTEM_DESIGN.md §9.7, ADR-008).</summary>
public static class StartupGuards
{
    public static void EnsureLiveTradingDisabled(IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("Features:LiveTrading"))
        {
            throw new InvalidOperationException(
                "Features:LiveTrading is enabled, but real-money trading is disabled in the MVP and no live broker adapter exists.");
        }
    }
}
