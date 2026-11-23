using Astryx.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Invest.Application;

public sealed record ContributionJob : IJob
{
    public static string JobType => "invest.contribution.v1";
}

/// <summary>Hourly: makes every due recurring contribution and invests it (T-164).</summary>
public sealed class ContributionHandler(InvestService service, ILogger<ContributionHandler> logger) : IJobHandler<ContributionJob>
{
    public async Task HandleAsync(ContributionJob job, JobContext context, CancellationToken cancellationToken)
    {
        if (await service.ContributeDueAsync(cancellationToken) is var count and > 0)
        {
            logger.LogInformation("Made {Count} recurring contribution(s)", count);
        }
    }
}
