using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moinho.Web.Data;
using Moinho.Web.Models;

namespace Moinho.Tests.Infrastructure;

// The real app (same Program.cs, same EnsureCreated + seeder) pointed at a new, uniquely named
// database, so each test class starts from the seed. Databases aren't dropped afterwards: the
// whole container is thrown away at the end of the run.
public class MoinhoFactory(SqlServerFixture sql) : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@teste.local";
    public const string AdminPassword = "Admin_Teste_123!";
    public const string Password = "Senha_Teste_123!";

    private readonly string _database = "moinho_" + Guid.NewGuid().ToString("N");

    public FakeEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", sql.ConnectionStringFor(_database));
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Demo:Enabled", "true");
        builder.UseSetting("Demo:EnableErrorSimulation", "true");
        builder.UseSetting("Demo:NightlyReset", "false");
        builder.UseSetting("RateLimits:EmailPermitLimit", RateLimit.ToString());
        builder.UseSetting("RateLimits:DemoLoginPermitLimit", RateLimit.ToString());
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    // High by default: every test request comes from the same (empty) client IP.
    protected virtual int RateLimit => 1000;

    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<ApplicationUser> CreateUserAsync(string role, string? email = null, bool confirmed = true)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        email ??= $"{role.ToLowerInvariant()}.{Guid.NewGuid():N}@teste.local";
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Usuário de Teste", EmailConfirmed = confirmed, CreatedAt = DateTime.UtcNow };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }
}
