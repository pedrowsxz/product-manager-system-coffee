using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.WebEncoders;
using Moinho.Web.Areas.Identity;
using Moinho.Web.Data;
using Moinho.Web.Models;
using Moinho.Web.Services;
using Serilog;
using Serilog.Debugging;

// Used only until the host is built (and for a crash during startup). The host gets its own logger below.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var config = builder.Configuration;

    // The host's own logger (not the static Log.Logger), so each host, including each test server,
    // writes to its own database. The host disposes it on shutdown, which flushes the batched DB sink.
    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(config)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteErrorsToDatabase(config.GetConnectionString("Default")!),
        preserveStaticLogger: true);
    if (builder.Environment.IsDevelopment())
    {
        // Shows the sink's own failures (e.g. a column mismatch) that would otherwise be silent.
        SelfLog.Enable(Console.Error);
    }

    builder.Services.Configure<SeedOptions>(config.GetSection("Seed"));
    builder.Services.Configure<DemoOptions>(config.GetSection("Demo"));
    builder.Services.Configure<EmailOptions>(config.GetSection("Email"));
    builder.Services.AddSingleton(TimeProvider.System);

    builder.Services.AddDbContext<AppDbContext>(options => options
        .UseSqlServer(config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' não configurada."))
        // EF logs every failed command at Error level. Each failure also surfaces as an exception that
        // we either handle (a duplicate SKU) or log once in the error handler, so skip the duplicate.
        .ConfigureWarnings(w => w.Ignore(CoreEventId.SaveChangesFailed, RelationalEventId.CommandError)));

    // ---- Identity ----
    builder.Services
        .AddDefaultIdentity<ApplicationUser>(options =>
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.User.RequireUniqueEmail = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            options.Tokens.EmailConfirmationTokenProvider = "EmailConfirmation";
            options.Tokens.ChangeEmailTokenProvider = "EmailConfirmation";
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddErrorDescriber<PtBrIdentityErrorDescriber>()
        .AddTokenProvider<EmailConfirmationTokenProvider<ApplicationUser>>("EmailConfirmation");

    // Password-reset (and invitation) links: 2 h. Email confirmation links: 24 h.
    builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
    builder.Services.Configure<EmailConfirmationTokenProviderOptions>(o =>
    {
        o.Name = "EmailConfirmation";
        o.TokenLifespan = TimeSpan.FromHours(24);
    });

    // The auth cookie carries the user's roles and security stamp. By default the stamp is re-checked
    // only every 30 minutes, so a demoted, deactivated or deleted user would keep access that long.
    // Zero means "check on every request": one extra user lookup per request, fine for a back-office.
    builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

    builder.Services.ConfigureApplicationCookie(options =>
    {
        // Return a real 403 (rendered by the friendly status page) instead of redirecting to a page.
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
    builder.Services.AddScoped<AccountEmails>();

    // Antiforgery tokens, auth cookies and every emailed confirmation/reset link are encrypted with
    // these keys. They must survive a redeploy, so in Docker they live on a volume.
    var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Moinho");
    if (config["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    {
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    }

    // ---- Authorization ----
    builder.Services.AddAuthorizationBuilder()
        // Every endpoint requires a signed-in user unless it says [AllowAnonymous].
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
        .AddPolicy(Policies.RequireAdmin, policy => policy.RequireRole(Roles.Admin))
        .AddPolicy(Policies.RequireStaff, policy => policy.RequireRole(Roles.Admin, Roles.Staff));

    // ---- Behind a reverse proxy (Caddy) ----
    // Without this, Request.Scheme/Host are what Caddy used to reach the container (http://web:8080),
    // so the links we put in emails would be wrong. Only proxies on the listed networks are trusted.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        foreach (var network in config.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
        // In production, only accept the public domain as X-Forwarded-Host, so nobody can make
        // the reset links in our emails point at their own site.
        foreach (var host in config.GetSection("ForwardedHeaders:AllowedHosts").Get<string[]>() ?? [])
        {
            options.AllowedHosts.Add(host);
        }
    });

    // ---- Rate limiting ----
    // Register, forgot password and resend confirmation send an email to any address a visitor types.
    // Limit those POSTs per client IP so the page can't be used to spam people or burn the email quota.
    builder.Services.AddRateLimiter(options =>
    {
        var limits = config.GetSection("RateLimits").Get<RateLimitOptions>() ?? new RateLimitOptions();
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(RateLimitPolicies.Email, context => PostsPerIp(context, limits.EmailPermitLimit, limits.EmailWindowMinutes));
        options.AddPolicy(RateLimitPolicies.AdminEdit, context => PostsPerIp(context, limits.AdminEditPermitLimit, limits.AdminEditWindowMinutes));
        options.AddPolicy(RateLimitPolicies.DemoLogin, context => PostsPerIp(context, limits.DemoLoginPermitLimit, limits.DemoLoginWindowMinutes));

        // Counts POSTs per client IP in a fixed window; GETs (just viewing the form) are never limited.
        static RateLimitPartition<string> PostsPerIp(HttpContext context, int permitLimit, int windowMinutes) =>
            HttpMethods.IsPost(context.Request.Method)
                ? RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(windowMinutes) })
                : RateLimitPartition.GetNoLimiter("get");
    });

    // ---- MVC + Razor Pages ----
    builder.Services.AddControllersWithViews(options =>
    {
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
        PtBrMessages.Apply(options.ModelBindingMessageProvider);
    });
    builder.Services.AddRazorPages(options =>
    {
        // The Identity UI library ships many pages we don't use (2FA, external logins, personal data...).
        // Removing their routes makes them 404 instead of silently reachable.
        options.Conventions.AddAreaFolderRouteModelConvention("Identity", "/", model =>
        {
            if (!IdentityPages.Used.Contains(model.ViewEnginePath))
            {
                model.Selectors.Clear();
            }
        });
    });
    // Write "Café" instead of "Caf&#xE9;" in the HTML (Razor still encodes <, >, &, quotes).
    builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

    builder.Services.AddScoped<Seeder>();
    builder.Services.AddScoped<ProductService>();
    builder.Services.AddScoped<StaffService>();
    builder.Services.AddScoped<DemoReset>();
    if (config.GetValue<bool>("Demo:NightlyReset"))
    {
        builder.Services.AddHostedService<DemoResetService>();
    }

    var app = builder.Build();

    await DbInitializer.InitializeAsync(app.Services);

    // Must run first so everything after it (links, rate-limit partitions, logs) sees the real client.
    app.UseForwardedHeaders();
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

    app.UseRateLimiter();
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
