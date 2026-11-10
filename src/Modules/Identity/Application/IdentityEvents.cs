using Astryx.Contracts.Events;

namespace Astryx.Modules.Identity.Application;

/// <summary>Send (or resend) a verification email. Carries no token: the handler issues one, so no secret sits in the outbox.</summary>
public sealed record EmailVerificationRequested(Guid UserId) : IIntegrationEvent
{
    public static string EventName => "identity.email_verification_requested.v1";
}

/// <summary>Someone tried to register an email that already has an account; tell the owner instead of revealing it to the caller.</summary>
public sealed record ExistingAccountRegistrationAttempted(Guid UserId) : IIntegrationEvent
{
    public static string EventName => "identity.existing_account_registration_attempted.v1";
}
