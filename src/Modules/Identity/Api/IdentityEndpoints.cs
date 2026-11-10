using System.Net.Mail;
using Astryx.Infrastructure.Http;
using Astryx.Modules.Identity.Application;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Identity.Api;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record RegisterResponse(bool EmailVerificationRequired);

public sealed record VerifyEmailRequest(Guid UserId, string Token);

public sealed record ResendVerificationRequest(string Email);

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
            .WithValidation<RegisterRequest>()
            .Produces<RegisterResponse>(StatusCodes.Status201Created);

        auth.MapPost("/verify-email", async (VerifyEmailRequest request, RegistrationService service, CancellationToken ct) =>
            {
                var result = await service.VerifyEmailAsync(new VerifyEmailCommand(request.UserId, request.Token), ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .WithName("VerifyEmail")
            .WithValidation<VerifyEmailRequest>()
            .Produces(StatusCodes.Status204NoContent);

        auth.MapPost("/resend-verification", async (ResendVerificationRequest request, RegistrationService service, CancellationToken ct) =>
            {
                await service.ResendVerificationAsync(request.Email, ct);
                return Results.Accepted();
            })
            .WithName("ResendVerification")
            .WithValidation<ResendVerificationRequest>()
            .Produces(StatusCodes.Status202Accepted);

        return app;
    }
}
