using Astryx.Contracts.Email;
using Astryx.Contracts.Events;
using Astryx.Modules.Identity.Domain;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Identity.Application;

/// <summary>
/// Issues a single-use token for a user and emails a link containing it (verification, password reset).
/// Earlier unused tokens of the same purpose are invalidated, so only the newest link works.
/// <para>
/// The token is committed only after the email was handed to SMTP, inside one transaction: if sending fails, nothing
/// is stored and the outbox retry issues a fresh token. A committed token for the outbox message therefore means
/// "already sent", which makes redeliveries no-ops. (If the commit fails after a successful send, that email's link
/// is dead and the retry sends a working one.)
/// </para>
/// </summary>
public sealed class TokenEmailIssuer(IdentityDbContext db, IEmailSender email, TimeProvider timeProvider)
{
    public async Task IssueAndSendAsync(
        UserAccount user, string purpose, TimeSpan lifetime, Guid sourceMessageId, Func<string, EmailMessage> buildEmail, CancellationToken cancellationToken)
    {
        if (await db.UserTokens.AnyAsync(t => t.SourceMessageId == sourceMessageId, cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.UserTokens
            .Where(t => t.UserId == user.Id && t.Purpose == purpose && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken);

        var (plain, hash) = SecureToken.Create();
        db.UserTokens.Add(new UserToken
        {
            Id = Ids.New(timeProvider),
            UserId = user.Id,
            Purpose = purpose,
            TokenHash = hash,
            ExpiresAt = now + lifetime,
            SourceMessageId = sourceMessageId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);

        await email.SendAsync(buildEmail(plain), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class EmailVerificationHandler(IdentityDbContext db, TokenEmailIssuer issuer, IOptions<AccountOptions> options)
    : IIntegrationEventHandler<EmailVerificationRequested>
{
    public async Task HandleAsync(EmailVerificationRequested integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == integrationEvent.UserId, cancellationToken);
        if (user is null || user.EmailConfirmed || user.DeletedAt is not null)
        {
            return;
        }

        await issuer.IssueAndSendAsync(user, TokenPurpose.EmailVerification, options.Value.EmailVerificationLifetime, context.MessageId, token =>
        {
            var link = $"{options.Value.AppLinkBase}verify-email?userId={user.Id}&token={token}";
            return new EmailMessage(
                user.Email,
                "Verify your Astryx email",
                $"Confirm your email to start using Astryx:\n\n{link}\n\nThis link expires in 24 hours. If you didn't create an account, ignore this email.",
                $"<p>Confirm your email to start using Astryx.</p><p><a href=\"{link}\">Verify email</a></p><p>This link expires in 24 hours. If you didn't create an account, ignore this email.</p>");
        }, cancellationToken);
    }
}

public sealed class PasswordResetEmailHandler(IdentityDbContext db, TokenEmailIssuer issuer, IOptions<AccountOptions> options)
    : IIntegrationEventHandler<PasswordResetRequested>
{
    public async Task HandleAsync(PasswordResetRequested integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == integrationEvent.UserId, cancellationToken);
        if (user is null || user.DeletedAt is not null)
        {
            return;
        }

        await issuer.IssueAndSendAsync(user, TokenPurpose.PasswordReset, options.Value.PasswordResetLifetime, context.MessageId, token =>
        {
            var link = $"{options.Value.AppLinkBase}reset-password?userId={user.Id}&token={token}";
            return new EmailMessage(
                user.Email,
                "Reset your Astryx password",
                $"Use this link to choose a new password:\n\n{link}\n\nIt expires in 30 minutes and works once. If you didn't ask for this, ignore this email; your password stays the same.",
                $"<p>Use this link to choose a new password.</p><p><a href=\"{link}\">Reset password</a></p><p>It expires in 30 minutes and works once. If you didn't ask for this, ignore this email; your password stays the same.</p>");
        }, cancellationToken);
    }
}

/// <summary>Confirms a password change to the owner, so an unexpected change is noticed.</summary>
public sealed class PasswordChangedEmailHandler(IdentityDbContext db, IEmailSender email) : IIntegrationEventHandler<PasswordChanged>
{
    public async Task HandleAsync(PasswordChanged integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == integrationEvent.UserId, cancellationToken);
        if (user is null || user.DeletedAt is not null)
        {
            return;
        }

        await email.SendAsync(new EmailMessage(
            user.Email,
            "Your Astryx password was changed",
            $"Your Astryx password was {(integrationEvent.Method == "reset" ? "reset" : "changed")} and other devices were signed out. If this wasn't you, reset your password now and contact support."),
            cancellationToken);
    }
}

/// <summary>Tells an account owner that someone tried to register with their email.</summary>
public sealed class ExistingAccountEmailHandler(IdentityDbContext db, IEmailSender email) : IIntegrationEventHandler<ExistingAccountRegistrationAttempted>
{
    public async Task HandleAsync(ExistingAccountRegistrationAttempted integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == integrationEvent.UserId, cancellationToken);
        if (user is null || user.DeletedAt is not null)
        {
            return;
        }

        await email.SendAsync(new EmailMessage(
            user.Email,
            "You already have an Astryx account",
            "Someone tried to create an Astryx account with this email. You already have one: sign in, or reset your password if you've forgotten it. If this wasn't you, no action is needed."),
            cancellationToken);
    }
}
