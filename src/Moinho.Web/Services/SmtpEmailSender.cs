using System.Net;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using MimeKit;
using Moinho.Web.Models;

namespace Moinho.Web.Services;

// The IEmailSender that the Identity UI pages resolve. Sends over SMTP with MailKit:
// Mailpit in development, a transactional provider (Brevo, Resend) in production.
public partial class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var settings = options.Value;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlMessage, TextBody = ToPlainText(htmlMessage) }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 15_000 };
        await client.ConnectAsync(settings.Host, settings.Port, settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None);
        if (!string.IsNullOrEmpty(settings.User))
        {
            await client.AuthenticateAsync(settings.User, settings.Password ?? "");
        }
        await client.SendAsync(new SendRequest(message));
        await client.DisconnectAsync(quit: true);
    }

    // Plain-text alternative for clients that don't render HTML: links become "label: url".
    internal static string ToPlainText(string html)
    {
        var text = LinkTag().Replace(html, m => $"{m.Groups[2].Value}: {m.Groups[1].Value}");
        text = BlockEnd().Replace(text, "\n");
        text = StyleBlock().Replace(text, "");
        text = AnyTag().Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        return string.Join("\n\n", lines);
    }

    [GeneratedRegex("<a [^>]*href=\"([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex LinkTag();

    [GeneratedRegex("<br ?/?>|</p>|</h1>|</div>|</tr>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex("<(style|head)[^>]*>.*?</\\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex StyleBlock();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();
}
