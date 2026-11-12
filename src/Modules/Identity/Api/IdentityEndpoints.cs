using System.Net.Mail;
using System.Security.Claims;
using Astryx.Infrastructure.Http;
using Astryx.Modules.Identity.Application;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Identity.Api;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record RegisterResponse(bool EmailVerificationRequired);

public sealed record VerifyEmailRequest(Guid UserId, string Token);

public sealed record ResendVerificationRequest(string Email);

public sealed record LoginRequest(string Email, string Password, string DeviceId);

public sealed record RefreshRequest(string RefreshToken, string DeviceId);

public sealed record LogoutRequest(string RefreshToken);

public sealed record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, string TokenType = "Bearer");

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() =>
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254).Must(RegisterRequestValidator.BeAnEmail).WithMessage("Enter a valid email address.");
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.Token).NotEmpty().Matches("^[A-Za-z0-9_-]{43}$").WithMessage("Malformed token.");
        RuleFor(r => r.NewPassword).NotEmpty().Length(12, 128).WithMessage("Password must be 12–128 characters.");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(r => r.NewPassword).NotEmpty().Length(12, 128).WithMessage("Password must be 12–128 characters.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(128);
        RuleFor(r => r.DeviceId).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("deviceId must be 8–64 letters, digits or '-'.").Matches(DeviceIdPattern).WithMessage("deviceId must be 8–64 letters, digits or '-'.");
    }

    internal const string DeviceIdPattern = "^[A-Za-z0-9-]{8,64}$";
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(r => r.RefreshToken).NotEmpty().Matches("^[A-Za-z0-9_-]{43}$").WithMessage("Malformed refresh token.");
        RuleFor(r => r.DeviceId).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("deviceId must be 8–64 letters, digits or '-'.").Matches(LoginRequestValidator.DeviceIdPattern).WithMessage("deviceId must be 8–64 letters, digits or '-'.");
    }
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator() =>
        RuleFor(r => r.RefreshToken).NotEmpty().Matches("^[A-Za-z0-9_-]{43}$").WithMessage("Malformed refresh token.");
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254).Must(BeAnEmail).WithMessage("Enter a valid email address.");
        RuleFor(r => r.Password).NotEmpty().Length(12, 128).WithMessage("Password must be 12–128 characters.");
        RuleFor(r => r.DisplayName).MaximumLength(80);
    }

    internal static bool BeAnEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && MailAddress.TryCreate(value.Trim(), out var address) && address.Address == value.Trim() && address.Host.Contains('.', StringComparison.Ordinal);
}

public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.Token).NotEmpty().Matches("^[A-Za-z0-9_-]{43}$").WithMessage("Malformed token.");
    }
}

public sealed class ResendVerificationRequestValidator : AbstractValidator<ResendVerificationRequest>
{
    public ResendVerificationRequestValidator() =>
        RuleFor(r => r.Email).NotEmpty().MaximumLength(254).Must(RegisterRequestValidator.BeAnEmail).WithMessage("Enter a valid email address.");
}

/// <summary>Routes under <c>/api/v1/auth</c> (SYSTEM_DESIGN.md §11.3).</summary>
public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        auth.MapPost("/register", async (RegisterRequest request, RegistrationService service, CancellationToken ct) =>
            {
                await service.RegisterAsync(new RegisterCommand(request.Email, request.Password, request.DisplayName), ct);
                return Results.Json(new RegisterResponse(EmailVerificationRequired: true), statusCode: StatusCodes.Status201Created);
            })
            .WithName("Register")
            .RequireRateLimiting(RateLimitPolicies.AuthStrict)
            .WithValidation<RegisterRequest>()
            .Produces<RegisterResponse>(StatusCodes.Status201Created);

        auth.MapPost("/verify-email", async (VerifyEmailRequest request, RegistrationService service, CancellationToken ct) =>
            {
                var result = await service.VerifyEmailAsync(new VerifyEmailCommand(request.UserId, request.Token), ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .WithName("VerifyEmail")
            .RequireRateLimiting(RateLimitPolicies.AuthStrict)
            .WithValidation<VerifyEmailRequest>()
            .Produces(StatusCodes.Status204NoContent);

        auth.MapPost("/resend-verification", async (ResendVerificationRequest request, RegistrationService service, CancellationToken ct) =>
            {
                await service.ResendVerificationAsync(request.Email, ct);
                return Results.Accepted();
            })
            .WithName("ResendVerification")
            .RequireRateLimiting(RateLimitPolicies.AuthStrict)
            .WithValidation<ResendVerificationRequest>()
            .Produces(StatusCodes.Status202Accepted);

        auth.MapPost("/login", async (LoginRequest request, SessionService sessions, CancellationToken ct) =>
                (await sessions.LoginAsync(request.Email, request.Password, request.DeviceId, ct)).Match(ToResponse, e => e.ToProblem()))
            .WithName("Login")
            .RequireRateLimiting(RateLimitPolicies.AuthStrict)
            .WithValidation<LoginRequest>()
            .Produces<TokenResponse>();

        auth.MapPost("/refresh", async (RefreshRequest request, SessionService sessions, CancellationToken ct) =>
                (await sessions.RefreshAsync(request.RefreshToken, request.DeviceId, ct)).Match(ToResponse, e => e.ToProblem()))
            .WithName("RefreshToken")
            .WithValidation<RefreshRequest>()
            .Produces<TokenResponse>();

        auth.MapPost("/logout", async (LogoutRequest request, ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
            {
                await sessions.LogoutAsync(user.UserId(), request.RefreshToken, ct);
                return Results.NoContent();
            })
            .WithName("Logout")
            .RequireAuthorization(AuthenticationSetup.AuthenticatedPolicy)
            .WithValidation<LogoutRequest>();

        auth.MapPost("/logout-all", async (ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
            {
                await sessions.LogoutAllAsync(user.UserId(), ct);
                return Results.NoContent();
            })
            .WithName("LogoutAll")
            .RequireAuthorization(AuthenticationSetup.AuthenticatedPolicy);

        auth.MapPost("/forgot-password", async (ForgotPasswordRequest request, PasswordService passwords, CancellationToken ct) =>
            {
                await passwords.RequestResetAsync(request.Email, ct);
                return Results.Accepted();
            })
            .WithName("ForgotPassword")
            .RequireRateLimiting(RateLimitPolicies.AuthStrict)
            .WithValidation<ForgotPasswordRequest>()
            .Produces(StatusCodes.Status202Accepted);

        auth.MapPost("/reset-password", async (ResetPasswordRequest request, PasswordService passwords, CancellationToken ct) =>
            {
                var result = await passwords.ResetAsync(request.UserId, request.Token, request.NewPassword, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .WithName("ResetPassword")
            .RequireRateLimiting(RateLimitPolicies.AuthStrict)
            .WithValidation<ResetPasswordRequest>()
            .Produces(StatusCodes.Status204NoContent);

        app.MapPost("/api/v1/me/change-password", async (ChangePasswordRequest request, ClaimsPrincipal user, PasswordService passwords, CancellationToken ct) =>
            {
                var familyId = Guid.Parse(user.FindFirstValue(AstryxClaims.Session)!);
                var result = await passwords.ChangeAsync(user.UserId(), familyId, request.CurrentPassword, request.NewPassword, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .WithName("ChangePassword")
            .WithTags("Me")
            .RequireAuthorization(AuthenticationSetup.AuthenticatedPolicy)
            .WithValidation<ChangePasswordRequest>()
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    private static IResult ToResponse(TokenPair pair) =>
        Results.Ok(new TokenResponse(pair.AccessToken, pair.AccessTokenExpiresAt, pair.RefreshToken, pair.RefreshTokenExpiresAt));
}
