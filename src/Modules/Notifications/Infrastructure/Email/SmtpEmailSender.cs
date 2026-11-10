using Astryx.Contracts.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Astryx.Modules.Notifications.Infrastructure.Email;

/// <summary>Bound from configuration section <c>Email:Smtp</c>. Locally this points at Mailpit (port 1025).</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public bool UseTls { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string From { get; set; } = "Astryx <no-reply@astryx.local>";
}

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(settings.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrEmpty(settings.Username))
        {
            await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        logger.LogInformation("Sent email '{Subject}'", message.Subject); // recipient intentionally not logged (§18.15)
    }
}
