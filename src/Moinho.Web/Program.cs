using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;
using Moinho.Web.Data;
using Moinho.Web.Models;
using Serilog;

// Used only until the host is built (and for a crash during startup). The host gets its own logger below.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var config = builder.Configuration;

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(config)
        .Enrich.FromLogContext()
        .WriteTo.Console(),
        preserveStaticLogger: true);

    builder.Services.Configure<SeedOptions>(config.GetSection("Seed"));
    builder.Services.Configure<DemoOptions>(config.GetSection("Demo"));
    builder.Services.AddSingleton(TimeProvider.System);

    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(config.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' não configurada.")));

    builder.Services
        .AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>();

    builder.Services.AddAuthorizationBuilder()
        // Every endpoint requires a signed-in user unless it says [AllowAnonymous].
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
        .AddPolicy("RequireAdmin", policy => policy.RequireRole(Roles.Admin))
        .AddPolicy("RequireStaff", policy => policy.RequireRole(Roles.Admin, Roles.Staff));

    builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
    builder.Services.AddRazorPages();
    // Write "Café" instead of "Caf&#xE9;" in the HTML (Razor still encodes <, >, &, quotes).
    builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

    builder.Services.AddScoped<Seeder>();

    var app = builder.Build();

    await DbInitializer.InitializeAsync(app.Services);

    app.UseExceptionHandler("/Erro");
    app.UseStatusCodePagesWithReExecute("/Erro/{0}");
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseSerilogRequestLogging();
    app.UseRouting();

    // Only pt-BR is supported, so numbers and dates are always parsed and shown the Brazilian way.
    var ptBr = new CultureInfo("pt-BR");
    app.UseRequestLocalization(new RequestLocalizationOptions
    {
        DefaultRequestCulture = new RequestCulture(ptBr),
        SupportedCultures = [ptBr],
        SupportedUICultures = [ptBr],
    });

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapStaticAssets().AllowAnonymous();
    app.MapGet("/", () => Results.Redirect("/Produtos"));
    app.MapControllers();
    app.MapRazorPages();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "A aplicação falhou ao iniciar");
    throw;
}
finally
{
    // Flush anything still buffered before the process exits.
    await Log.CloseAndFlushAsync();
}

public partial class Program;
