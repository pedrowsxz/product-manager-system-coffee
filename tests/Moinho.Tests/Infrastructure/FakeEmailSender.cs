using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace Moinho.Tests.Infrastructure;

public record SentEmail(string To, string Subject, string Html)
{
    // The first link in the email (the button), HTML-decoded so it can be requested.
    public string Link => WebUtility.HtmlDecode(Regex.Match(Html, "href=\"([^\"]+)\"").Groups[1].Value);
}

// Captures emails instead of sending them. Set Fail = true to simulate an SMTP outage.
public class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<SentEmail> Sent { get; } = new();

    public bool Fail { get; set; }

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (Fail)
        {
            throw new InvalidOperationException("SMTP indisponível (simulado)");
        }
        Sent.Enqueue(new SentEmail(email, subject, htmlMessage));
        return Task.CompletedTask;
    }

    public SentEmail LastTo(string email) => Sent.Last(e => e.To.Equals(email, StringComparison.OrdinalIgnoreCase));

    public int CountTo(string email) => Sent.Count(e => e.To.Equals(email, StringComparison.OrdinalIgnoreCase));
}
