using Astryx.Contracts.Paper;
using Astryx.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Paper.Application;

/// <summary><see cref="IPositionCloser"/>: the user's close path in its own transaction, for strategy exits (T-163).</summary>
public sealed class PositionCloser(IServiceProvider services, ILogger<PositionCloser> logger) : IPositionCloser
{
    public async Task<bool> CloseAsync(Guid userId, Guid positionId, string idempotencyKey, string exitReason, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ModuleUnitOfWork>();
        await unit.BeginAsync(cancellationToken);
        var result = await scope.ServiceProvider.GetRequiredService<PositionActions>().CloseForAsync(userId, positionId, idempotencyKey, exitReason, cancellationToken);
        if (!result.IsSuccess)
        {
            logger.LogInformation("Strategy exit of position {PositionId} not done: {Code}", positionId, result.Error.Code);
            return false;
        }

        await unit.CommitAsync(cancellationToken);
        return true;
    }
}
