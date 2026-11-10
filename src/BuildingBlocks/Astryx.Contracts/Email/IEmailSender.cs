namespace Astryx.Contracts.Email;

/// <summary>Sends transactional email (verification, password reset). Implemented by the Notifications module.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);
