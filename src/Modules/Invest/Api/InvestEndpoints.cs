using Astryx.Infrastructure.Http;
using Astryx.Infrastructure.Idempotency;
using Astryx.Modules.Invest.Application;
using Astryx.Modules.Invest.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Invest.Api;

public sealed class SaveInvestPlanRequestValidator : AbstractValidator<SaveInvestPlanRequest>
{
    public SaveInvestPlanRequestValidator()
    {
        RuleFor(r => r.Profile).Must(p => Mixes.Targets.ContainsKey(p)).WithMessage("Choose cautious, balanced or growth.");
        RuleFor(r => r.InitialDeposit).InclusiveBetween(100m, 1_000_000m).When(r => r.InitialDeposit is not null);
        RuleFor(r => r.ContributionFrequency).Must(f => Frequencies.All.Contains(f)).WithMessage("Choose none, weekly or monthly.");
        RuleFor(r => r.ContributionAmount).InclusiveBetween(0m, 100_000m);
        RuleFor(r => r.ContributionAmount).GreaterThanOrEqualTo(10m).When(r => r.ContributionFrequency != Frequencies.None).WithMessage("Contribute at least $10.");
        RuleFor(r => r.GoalAmount).GreaterThan(0m).LessThanOrEqualTo(100_000_000m).When(r => r.GoalAmount is not null);
    }
}

/// <summary>I1–I4 (TASKS.md T-164): the long-term investing plan.</summary>
public static class InvestEndpoints
{
    public static IEndpointRouteBuilder MapInvestEndpoints(this IEndpointRouteBuilder app)
    {
        var invest = app.MapGroup("/api/v1/invest").WithTags("Invest").RequireAuthorization("Authenticated");

        invest.MapGet("/", async (InvestService service, CancellationToken ct) => (await service.GetAsync(ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("GetInvestPlan").Produces<InvestDto>().ProducesProblem(StatusCodes.Status404NotFound);

        invest.MapPut("/", async (SaveInvestPlanRequest request, InvestService service, CancellationToken ct) => (await service.SaveAsync(request, ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("SaveInvestPlan").WithValidation<SaveInvestPlanRequest>().RequireIdempotency().Produces<InvestDto>().ProducesProblem(StatusCodes.Status409Conflict);

        invest.MapPost("/invest-cash", async (InvestService service, CancellationToken ct) => (await service.InvestNowAsync(ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("InvestCash").RequireIdempotency().Produces<InvestDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        invest.MapPost("/rebalance", async (InvestService service, CancellationToken ct) => (await service.RebalanceAsync(ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("RebalanceInvestPlan").RequireIdempotency().Produces<InvestDto>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
