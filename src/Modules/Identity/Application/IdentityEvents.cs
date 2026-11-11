using Astryx.Contracts.Events;

namespace Astryx.Modules.Identity.Application;

/// <summary>Send (or resend) a verification email. Carries no token: the handler issues one, so no secret sits in the outbox.</summary>
public sealed record EmailVerificationRequested(Guid UserId) : IIntegrationEvent
{
    public static string EventName => "identity.email_verification_requested.v1";
}

/// <summary>Send a password-reset email. Like verification, the token is issued by the handler, never carried in the outbox.</summary>
public sealed record PasswordResetRequested(Guid UserId) : IIntegrationEvent
{
    public static string EventName => "identity.password_reset_requested.v1";
}

/// <summary>The password was reset or changed; tell the owner so an unexpected change is noticed.</summary>
public sealed record PasswordChanged(Guid UserId, string Method) : IIntegrationEvent
{
    public static string EventName => "identity.password_changed.v1";
}

/// <summary>Someone tried to register an email that already has an account; tell the owner instead of revealing it to the caller.</summary>
public sealed record ExistingAccountRegistrationAttempted(Guid UserId) : IIntegrationEvent
{
    public static string EventName => "identity.existing_account_registration_attempted.v1";
}
