using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moinho.Tests.Infrastructure;
using Moinho.Web.Data;
using Moinho.Web.Models;
using Moinho.Web.Services;

namespace Moinho.Tests;

public class DemoLoginTests(MoinhoFactory factory) : IClassFixture<MoinhoFactory>
{
    [Fact]
    public async Task Login_page_shows_both_demo_buttons_and_the_register_hint()
    {
        var html = await (await factory.CreateBrowser().GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync();

        Assert.Contains("Entrar como Admin (demo)", html);
        Assert.Contains("Entrar como Equipe (demo)", html);
        Assert.Contains("você receberá um email de confirmação de verdade", html);
    }

    [Fact]
    public async Task Admin_demo_button_signs_into_the_protected_admin_account()
    {
        var client = factory.CreateBrowser();

        var response = await client.PostFormAsync("/Identity/Account/Login", new(), postUrl: "/demo/entrar/admin");

        Assert.Equal("/Admin", response.Headers.Location!.OriginalString);
        var panel = await client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        Assert.Contains(DemoAccounts.AdminEmail, await panel.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Staff_demo_button_signs_in_without_Admin_access()
    {
        var client = factory.CreateBrowser();

        await client.PostFormAsync("/Identity/Account/Login", new(), postUrl: "/demo/entrar/equipe");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Produtos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin")).StatusCode);
    }

    [Fact]
    public async Task Demo_sign_in_needs_an_antiforgery_token_and_a_known_account()
    {
        var client = factory.CreateBrowser();

        var noToken = await client.PostAsync("/demo/entrar/admin", new FormUrlEncodedContent([]));
        var unknown = await client.PostFormAsync("/Identity/Account/Login", new(), postUrl: "/demo/entrar/root");

        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}

public class DemoResetTests(MoinhoFactory factory) : IClassFixture<MoinhoFactory>
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    [Theory]
    [InlineData("2026-03-10T03:59:00", "2026-03-10T04:00:00")]
    [InlineData("2026-03-10T04:00:00", "2026-03-11T04:00:00")]
    [InlineData("2026-03-10T23:30:00", "2026-03-11T04:00:00")]
    public void Next_run_is_the_next_4am_in_Sao_Paulo(string localNow, string expectedLocal)
    {
        var now = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.Parse(localNow), SaoPaulo), TimeSpan.Zero);

        var next = DemoResetService.NextRun(now);

        Assert.Equal(DateTime.Parse(expectedLocal), TimeZoneInfo.ConvertTime(next, SaoPaulo).DateTime);
    }

    [Fact]
    public async Task Nightly_reset_restores_exactly_the_seed_counts()
    {
        // Make the demo "dirty": a new product, a self-registered user and an error row.
        var staff = await factory.CreateBrowser().PostFormAsync("/Identity/Account/Login", new(), postUrl: "/demo/entrar/equipe");
        await factory.WithDbAsync(async db =>
        {
            db.Products.Add(new Product { Name = "Produto extra", Sku = "EXTRA-001", Price = 1, DateAdded = DateTime.UtcNow });
            db.ErrorLogs.Add(new ErrorLog { Level = "Error", Message = "teste", TimeStamp = DateTime.UtcNow });
            return await db.SaveChangesAsync();
        });
        await factory.CreateUserAsync(Roles.Staff);
        Assert.NotEqual(Seeder.ProductCount, await factory.WithDbAsync(db => db.Products.CountAsync()));

        // Start the real background service with a fake clock just before 04:00 in São Paulo...
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 6, 59, 0, TimeSpan.Zero)); // 03:59 in São Paulo
        var service = new DemoResetService(factory.Services.GetRequiredService<IServiceScopeFactory>(), clock,
            factory.Services.GetRequiredService<ILogger<DemoResetService>>());
        await service.StartAsync(CancellationToken.None);

        // ...and move it past 04:00. Since .NET 10, ExecuteAsync runs on a background thread, so give it
        // a moment to read the clock and start waiting before the clock moves.
        await Task.Delay(1000);
        clock.Advance(TimeSpan.FromMinutes(2));
        var products = 0;
        for (var i = 0; i < 60 && products != Seeder.ProductCount; i++)
        {
            await Task.Delay(500);
            products = await factory.WithDbAsync(db => db.Products.CountAsync());
        }
        await Task.Delay(2000); // let the seeder finish the users too
        await service.StopAsync(CancellationToken.None);

        var (users, roles, logs, extra) = await factory.WithDbAsync(async db => (
            await db.Users.CountAsync(), await db.Roles.CountAsync(), await db.ErrorLogs.CountAsync(),
            await db.Products.CountAsync(p => p.Sku == "EXTRA-001")));
        Assert.Equal(Seeder.ProductCount, products);
        Assert.Equal(3 + Seeder.StaffCount, users);
        Assert.Equal(2, roles);
        Assert.Equal(0, logs);
        Assert.Equal(0, extra);
    }
}
