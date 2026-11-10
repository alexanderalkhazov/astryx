using Astryx.Contracts.Email;
using Astryx.Contracts.Events;
using Astryx.Modules.Identity.Domain;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Identity.Application;

/// <summary>
/// Issues a verification token and emails the link. Earlier unused verification tokens for the user are invalidated,
/// so only the newest link works.
/// <para>
/// The token is committed only after the email was handed to SMTP, inside one transaction: if sending fails, nothing
/// is stored and the outbox retry issues a fresh token. A committed token for this outbox message therefore means
/// "already sent", which makes redeliveries no-ops. (If the commit fails after a successful send, that email's link
/// is dead and the retry sends a working one.)
/// </para>
/// </summary>
public sealed class EmailVerificationHandler(IdentityDbContext db, IEmailSender email, IOptions<AccountOptions> options, TimeProvider timeProvider)
    : IIntegrationEventHandler<EmailVerificationRequested>
{
    public async Task HandleAsync(EmailVerificationRequested integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        if (await db.UserTokens.AnyAsync(t => t.SourceMessageId == context.MessageId, cancellationToken))
        {
            return;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == integrationEvent.UserId, cancellationToken);
        if (user is null || user.EmailConfirmed || user.DeletedAt is not null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.UserTokens
            .Where(t => t.UserId == user.Id && t.Purpose == TokenPurpose.EmailVerification && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken);

        var (plain, hash) = SecureToken.Create();
        db.UserTokens.Add(new UserToken
        {
            Id = Ids.New(timeProvider),
            UserId = user.Id,
            Purpose = TokenPurpose.EmailVerification,
            TokenHash = hash,
            ExpiresAt = now + options.Value.EmailVerificationLifetime,
            SourceMessageId = context.MessageId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);

        var link = $"{options.Value.AppLinkBase}verify-email?userId={user.Id}&token={plain}";
        await email.SendAsync(new EmailMessage(
            user.Email,
            "Verify your Astryx email",
            $"Confirm your email to start using Astryx:\n\n{link}\n\nThis link expires in 24 hours. If you didn't create an account, ignore this email.",
            $"<p>Confirm your email to start using Astryx.</p><p><a href=\"{link}\">Verify email</a></p><p>This link expires in 24 hours. If you didn't create an account, ignore this email.</p>"),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
