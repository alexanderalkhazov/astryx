using System.Security.Claims;
using System.Text.RegularExpressions;
using Astryx.Contracts.Identity;
using Astryx.Infrastructure.Http;
using Astryx.Modules.Profiles.Application;
using Astryx.Modules.Profiles.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using NodaTime;

namespace Astryx.Modules.Profiles.Api;

public sealed record FeatureFlags(bool LiveTrading, bool StrategyConfigure, bool Push);

public sealed record CurrentUserResponse(
    Guid Id, string Email, bool EmailVerified, string? DisplayName, string Timezone, string Locale, bool BiometricLock, string TradingMode, FeatureFlags Features);

public sealed record UpdateProfileRequest(string? DisplayName, string? Timezone, string? Locale, bool? BiometricLock);

public sealed record BoundsDto<T>(T Min, T Max);

public sealed record RiskBoundsDto(
    BoundsDto<decimal> MaxRiskPerTradePct,
    BoundsDto<decimal> MaxPositionWeightPct,
    BoundsDto<decimal> MaxSectorExposurePct,
    BoundsDto<decimal> DailyLossHaltPct,
    BoundsDto<int> MaxOpenPositions,
    BoundsDto<decimal> MinRewardRisk);

public sealed record RiskSettingsDto(
    decimal MaxRiskPerTradePct,
    decimal MaxPositionWeightPct,
    decimal MaxSectorExposurePct,
    decimal SectorWarningPct,
    decimal DailyLossHaltPct,
    int MaxOpenPositions,
    decimal MinRewardRisk,
    int Version,
    RiskBoundsDto Bounds);

public sealed record UpdateRiskSettingsRequest(
    decimal MaxRiskPerTradePct,
    decimal MaxPositionWeightPct,
    decimal MaxSectorExposurePct,
    decimal SectorWarningPct,
    decimal DailyLossHaltPct,
    int MaxOpenPositions,
    decimal MinRewardRisk);

public sealed record NotificationPreferencesDto(bool Fills, bool Signals, bool StopProximity, bool StrategyGuard, bool System, bool DailySummary, bool PushEnabled);

public sealed record TradingModeRequest(string Mode, string? Confirmation);

public sealed record TradingModeResponse(string Mode);

public sealed partial class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(r => r.DisplayName).MaximumLength(80);
        RuleFor(r => r.Timezone)
            .Must(tz => tz is null || DateTimeZoneProviders.Tzdb.GetZoneOrNull(tz) is not null)
            .WithMessage("Use an IANA time zone such as America/New_York.");
        RuleFor(r => r.Locale).Must(l => l is null || LocalePattern().IsMatch(l)).WithMessage("Use a locale such as en-US.");
    }

    [GeneratedRegex("^[a-z]{2,3}(-[A-Z]{2})?$")]
    private static partial Regex LocalePattern();
}

public sealed class UpdateRiskSettingsRequestValidator : AbstractValidator<UpdateRiskSettingsRequest>
{
    public UpdateRiskSettingsRequestValidator()
    {
        Pct(RuleFor(r => r.MaxRiskPerTradePct), RiskBounds.MaxRiskPerTradePct);
        Pct(RuleFor(r => r.MaxPositionWeightPct), RiskBounds.MaxPositionWeightPct);
        Pct(RuleFor(r => r.MaxSectorExposurePct), RiskBounds.MaxSectorExposurePct);
        Pct(RuleFor(r => r.DailyLossHaltPct), RiskBounds.DailyLossHaltPct);
        RuleFor(r => r.SectorWarningPct)
            .GreaterThan(0m)
            .Must((r, warning) => warning < r.MaxSectorExposurePct).WithMessage("The sector warning must be below the maximum sector exposure.")
            .Must(v => v.Scale <= RiskBounds.PctScale).WithMessage($"Use at most {RiskBounds.PctScale} decimal places.");
        RuleFor(r => r.MaxOpenPositions).InclusiveBetween(RiskBounds.MaxOpenPositions.Min, RiskBounds.MaxOpenPositions.Max);
        RuleFor(r => r.MinRewardRisk)
            .InclusiveBetween(RiskBounds.MinRewardRisk.Min, RiskBounds.MinRewardRisk.Max)
            .Must(v => v.Scale <= RiskBounds.RewardRiskScale).WithMessage($"Use at most {RiskBounds.RewardRiskScale} decimal places.");
    }

    private static void Pct(IRuleBuilder<UpdateRiskSettingsRequest, decimal> rule, Domain.Range<decimal> range) =>
        rule.InclusiveBetween(range.Min, range.Max)
            .WithMessage($"Must be between {range.Min} and {range.Max} (fractions: 0.0075 = 0.75%).")
            .Must(v => v.Scale <= RiskBounds.PctScale).WithMessage($"Use at most {RiskBounds.PctScale} decimal places.");
}

public sealed class TradingModeRequestValidator : AbstractValidator<TradingModeRequest>
{
    public TradingModeRequestValidator() =>
        RuleFor(r => r.Mode).Must(m => m is "paper" or "live").WithMessage("Mode must be 'paper' or 'live'.");
}

/// <summary>Routes under <c>/api/v1/me</c> (SYSTEM_DESIGN.md §11.3 A10, A11, S1, S2, S4, S5).</summary>
public static class ProfileEndpoints
{
    private static readonly RiskBoundsDto Bounds = new(
        new(RiskBounds.MaxRiskPerTradePct.Min, RiskBounds.MaxRiskPerTradePct.Max),
        new(RiskBounds.MaxPositionWeightPct.Min, RiskBounds.MaxPositionWeightPct.Max),
        new(RiskBounds.MaxSectorExposurePct.Min, RiskBounds.MaxSectorExposurePct.Max),
        new(RiskBounds.DailyLossHaltPct.Min, RiskBounds.DailyLossHaltPct.Max),
        new(RiskBounds.MaxOpenPositions.Min, RiskBounds.MaxOpenPositions.Max),
        new(RiskBounds.MinRewardRisk.Min, RiskBounds.MinRewardRisk.Max));

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/v1/me").WithTags("Me").RequireAuthorization("Authenticated");

        me.MapGet("", async (ClaimsPrincipal user, IUserAccountQuery accounts, ProfileService profiles, IConfiguration config, CancellationToken ct) =>
            {
                var userId = UserId(user);
                var account = await accounts.FindAsync(userId, ct);
                if (account is null)
                {
                    return Results.Problem(statusCode: 404, type: ErrorHttpMapping.TypeUri("not_found"));
                }

                return Results.Ok(ToCurrentUser(account, await profiles.GetProfileAsync(userId, ct), config));
            })
            .WithName("GetCurrentUser")
            .Produces<CurrentUserResponse>();

        me.MapPatch("", async (UpdateProfileRequest request, ClaimsPrincipal user, IUserAccountQuery accounts, ProfileService profiles, IConfiguration config, CancellationToken ct) =>
            {
                var userId = UserId(user);
                var profile = await profiles.UpdateProfileAsync(userId, new ProfileUpdate(request.DisplayName, request.Timezone, request.Locale, request.BiometricLock), ct);
                var account = await accounts.FindAsync(userId, ct);
                return Results.Ok(ToCurrentUser(account!, profile, config));
            })
            .WithName("UpdateCurrentUser")
            .WithValidation<UpdateProfileRequest>()
            .Produces<CurrentUserResponse>();

        me.MapGet("/settings/risk", async (ClaimsPrincipal user, ProfileService profiles, HttpContext http, CancellationToken ct) =>
            {
                var settings = await profiles.GetRiskSettingsAsync(UserId(user), ct);
                http.Response.Headers.ETag = ETags.Format(settings.Version);
                return Results.Ok(ToDto(settings));
            })
            .WithName("GetRiskSettings")
            .Produces<RiskSettingsDto>();

        me.MapPut("/settings/risk", async (UpdateRiskSettingsRequest request, ClaimsPrincipal user, ProfileService profiles, HttpContext http, CancellationToken ct) =>
            {
                var update = new RiskSettingsUpdate(request.MaxRiskPerTradePct, request.MaxPositionWeightPct, request.MaxSectorExposurePct,
                    request.SectorWarningPct, request.DailyLossHaltPct, request.MaxOpenPositions, request.MinRewardRisk);
                var result = await profiles.UpdateRiskSettingsAsync(UserId(user), ETags.ParseIfMatch(http), update, ct);
                if (!result.IsSuccess)
                {
                    return result.Error.ToProblem();
                }

                http.Response.Headers.ETag = ETags.Format(result.Value.Version);
                return Results.Ok(ToDto(result.Value));
            })
            .WithName("UpdateRiskSettings")
            .WithValidation<UpdateRiskSettingsRequest>()
            .Produces<RiskSettingsDto>()
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        me.MapGet("/settings/notifications", async (ClaimsPrincipal user, ProfileService profiles, CancellationToken ct) =>
                Results.Ok(ToDto(await profiles.GetNotificationsAsync(UserId(user), ct))))
            .WithName("GetNotificationPreferences")
            .Produces<NotificationPreferencesDto>();

        me.MapPut("/settings/notifications", async (NotificationPreferencesDto request, ClaimsPrincipal user, ProfileService profiles, CancellationToken ct) =>
                Results.Ok(ToDto(await profiles.UpdateNotificationsAsync(UserId(user),
                    new NotificationUpdate(request.Fills, request.Signals, request.StopProximity, request.StrategyGuard, request.System, request.DailySummary, request.PushEnabled), ct))))
            .WithName("UpdateNotificationPreferences")
            .Produces<NotificationPreferencesDto>();

        me.MapPut("/settings/trading-mode", async (TradingModeRequest request, ClaimsPrincipal user, ProfileService profiles, CancellationToken ct) =>
                (await profiles.SetTradingModeAsync(UserId(user), request.Mode, ct)).Match(mode => Results.Ok(new TradingModeResponse(mode)), e => e.ToProblem()))
            .WithName("SetTradingMode")
            .WithValidation<TradingModeRequest>()
            .Produces<TradingModeResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);

    private static CurrentUserResponse ToCurrentUser(UserAccountSummary account, UserProfile profile, IConfiguration config) =>
        new(account.Id, account.Email, account.EmailVerified, profile.DisplayName, profile.Timezone, profile.Locale, profile.BiometricLock, profile.TradingMode,
            new FeatureFlags(config.GetValue<bool>("Features:LiveTrading"), config.GetValue<bool>("Features:StrategyConfigure"), config.GetValue<bool>("Features:Push")));

    private static RiskSettingsDto ToDto(RiskSettings s) =>
        new(s.MaxRiskPerTradePct, s.MaxPositionWeightPct, s.MaxSectorExposurePct, s.SectorWarningPct, s.DailyLossHaltPct, s.MaxOpenPositions, s.MinRewardRisk, s.Version, Bounds);

    private static NotificationPreferencesDto ToDto(NotificationPreferences n) =>
        new(n.Fills, n.Signals, n.StopProximity, n.StrategyGuard, n.System, n.DailySummary, n.PushEnabled);
}
