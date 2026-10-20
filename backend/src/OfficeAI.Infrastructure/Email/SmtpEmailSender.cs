using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using OfficeAI.Application.Abstractions;

namespace OfficeAI.Infrastructure.Email;

public class EmailOptions
{
    /// <summary>SMTP server. Empty = email is off.</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;

    /// <summary>StartTls (port 587), SslOnConnect (port 465) or None (local test servers only).</summary>
    public string Security { get; set; } = "StartTls";
    public string? Username { get; set; }

    /// <summary>Set through the environment (Email__Password), never in a file that is committed.</summary>
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "AI Software House";
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private EmailOptions O => options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(O.Host) && MailboxAddress.TryParse(O.FromAddress, out _);
    public string? FromAddress => IsConfigured ? O.FromAddress : null;

    public async Task SendAsync(OutgoingEmail email, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Email is not configured on the server.");
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(O.FromName, O.FromAddress));
        message.To.Add(new MailboxAddress(email.ToName, email.To));
        message.Subject = email.Subject;
        var body = new BodyBuilder { TextBody = email.TextBody };
        if (email.Attachment is { } a)
            body.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
        message.Body = body.ToMessageBody();

        using var client = new SmtpClient { Timeout = O.TimeoutSeconds * 1000 };
        var security = O.Security switch
        {
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            "None" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };
        await client.ConnectAsync(O.Host, O.Port, security, ct);
        if (!string.IsNullOrEmpty(O.Username)) await client.AuthenticateAsync(O.Username, O.Password ?? "", ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
