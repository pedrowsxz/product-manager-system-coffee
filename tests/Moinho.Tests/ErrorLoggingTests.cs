using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Moinho.Tests.Infrastructure;
using Moinho.Web.Models;

namespace Moinho.Tests;

public class ErrorLoggingTests(MoinhoFactory factory) : IClassFixture<MoinhoFactory>
{
    // The sink writes in batches, so give it a few seconds.
    private async Task<List<ErrorLog>> WaitForRowsAsync(Func<ErrorLog, bool> match, int expected = 1)
    {
        List<ErrorLog> rows = [];
        for (var i = 0; i < 40; i++)
        {
            rows = (await factory.WithDbAsync(db => db.ErrorLogs.AsNoTracking().ToListAsync())).Where(match).ToList();
            if (rows.Count >= expected)
            {
                break;
            }
            await Task.Delay(500);
        }
        return rows;
    }

    [Fact]
    public async Task Simulated_error_shows_a_friendly_page_and_writes_exactly_one_row_with_the_TraceId()
    {
        var admin = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);

        var response = await admin.PostFormAsync("/Admin/Logs", new(), postUrl: "/Admin/Logs/SimularErro");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Algo deu errado", html);
        Assert.DoesNotContain("InvalidOperationException", html); // no stack trace for the visitor
        var traceId = Regex.Match(html, "data-testid=\"trace-id\">([0-9a-f]{32})<").Groups[1].Value;
        Assert.NotEmpty(traceId);

        var rows = await WaitForRowsAsync(r => r.TraceId == traceId);
        await Task.Delay(1500); // a late duplicate would show up here
        rows = await WaitForRowsAsync(r => r.TraceId == traceId);

        var row = Assert.Single(rows);
        Assert.Equal("Error", row.Level);
        Assert.Contains("/Admin/Logs/SimularErro", row.Message);
        Assert.Contains("Erro simulado", row.Exception);
        Assert.Contains("\"UserId\"", row.LogEvent);
        Assert.True(row.TimeStamp > DateTime.UtcNow.AddMinutes(-5) && row.TimeStamp <= DateTime.UtcNow.AddMinutes(1), "TimeStamp should be UTC");

        var list = await (await admin.GetAsync($"/Admin/Logs?traceId={traceId}")).Content.ReadAsStringAsync();
        Assert.Contains("1 registro(s)", list);
        var details = await (await admin.GetAsync($"/Admin/Logs/{row.Id}")).Content.ReadAsStringAsync();
        Assert.Contains(traceId, details);
        Assert.Contains("Erro simulado", details);
    }

    [Fact]
    public async Task Not_found_and_forbidden_responses_are_not_logged_as_errors()
    {
        var staff = await factory.CreateUserAsync(Roles.Staff);
        var client = await factory.CreateBrowser().LoggedInAsync(staff.Email!);
        var before = await factory.WithDbAsync(db => db.ErrorLogs.CountAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/nao-existe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Produtos/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin")).StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(6)); // longer than the sink's batch period

        Assert.Equal(before, await factory.WithDbAsync(db => db.ErrorLogs.CountAsync()));
    }

    [Fact]
    public async Task Email_delivery_failure_is_logged_to_the_database_without_the_link()
    {
        factory.Emails.Fail = true;
        try
        {
            var email = $"falha.{Guid.NewGuid():N}@teste.local";
            await factory.CreateBrowser().PostFormAsync("/Identity/Account/Register", new()
            {
                ["Input.FullName"] = "Falha Email",
                ["Input.Email"] = email,
                ["Input.Password"] = MoinhoFactory.Password,
                ["Input.ConfirmPassword"] = MoinhoFactory.Password,
            });

            var rows = await WaitForRowsAsync(r => r.Message!.Contains("Falha ao enviar email de"));

            var row = Assert.Single(rows);
            Assert.DoesNotContain("code=", row.LogEvent);
            Assert.DoesNotContain("ConfirmEmail", row.LogEvent);
            Assert.DoesNotContain(MoinhoFactory.Password, row.LogEvent);
        }
        finally
        {
            factory.Emails.Fail = false;
        }
    }
}
