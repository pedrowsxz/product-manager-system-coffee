using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moinho.Tests.Infrastructure;
using Moinho.Web.Data;
using Moinho.Web.Models;

namespace Moinho.Tests;

public class AccountFlowTests(MoinhoFactory factory) : IClassFixture<MoinhoFactory>
{
    private static string NewEmail() => $"novo.{Guid.NewGuid():N}@teste.local";

    private Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password = MoinhoFactory.Password) =>
        client.PostFormAsync("/Identity/Account/Register", new()
        {
            ["Input.FullName"] = "Maria Teste",
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        });

    private async Task<ApplicationUser?> FindAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    [Fact]
    public async Task Register_creates_unconfirmed_staff_user_and_sends_confirmation_link()
    {
        var email = NewEmail();

        var response = await RegisterAsync(factory.CreateBrowser(), email);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Identity/Account/RegisterConfirmation", response.Headers.Location!.OriginalString);
        var user = await FindAsync(email);
        Assert.NotNull(user);
        Assert.False(user.EmailConfirmed);
        using var scope = factory.Services.CreateScope();
        var roles = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync(user);
        Assert.Equal([Roles.Staff], roles);
        Assert.Contains("/Identity/Account/ConfirmEmail?userId=", factory.Emails.LastTo(email).Link);
    }

    [Fact]
    public async Task Login_is_refused_until_the_email_link_is_followed()
    {
        var email = NewEmail();
        var client = factory.CreateBrowser();
        await RegisterAsync(client, email);

        var before = await client.LoginAsync(email, MoinhoFactory.Password);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode); // the login form again, with an error
        Assert.Contains("Email ou senha inválidos", await before.Content.ReadAsStringAsync());

        var confirm = await client.GetAsync(factory.Emails.LastTo(email).Link);
        Assert.Contains("Email confirmado", await confirm.Content.ReadAsStringAsync());

        var after = await client.LoginAsync(email, MoinhoFactory.Password);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Produtos")).StatusCode);
    }

    [Fact]
    public async Task Register_with_an_existing_email_looks_the_same_and_sends_nothing()
    {
        var user = await factory.CreateUserAsync(Roles.Staff);
        var sentBefore = factory.Emails.CountTo(user.Email!);

        var response = await RegisterAsync(factory.CreateBrowser(), user.Email!);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Identity/Account/RegisterConfirmation", response.Headers.Location!.OriginalString);
        Assert.Equal(sentBefore, factory.Emails.CountTo(user.Email!));
    }

    [Fact]
    public async Task Forgot_password_link_resets_the_password_and_old_password_stops_working()
    {
        var user = await factory.CreateUserAsync(Roles.Staff);
        var client = factory.CreateBrowser();

        var forgot = await client.PostFormAsync("/Identity/Account/ForgotPassword", new() { ["Input.Email"] = user.Email! });
        Assert.Equal("/Identity/Account/ForgotPasswordConfirmation", forgot.Headers.Location!.OriginalString);

        var link = factory.Emails.LastTo(user.Email!).Link;
        var newPassword = "Nova_Senha_456!";
        var reset = await client.PostFormAsync(link, new()
        {
            ["Input.Email"] = user.Email!,
            ["Input.Password"] = newPassword,
            ["Input.ConfirmPassword"] = newPassword,
        }, postUrl: "/Identity/Account/ResetPassword");
        Assert.Equal("/Identity/Account/ResetPasswordConfirmation", reset.Headers.Location!.OriginalString);

        Assert.Equal(HttpStatusCode.OK, (await factory.CreateBrowser().LoginAsync(user.Email!, MoinhoFactory.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await factory.CreateBrowser().LoginAsync(user.Email!, newPassword)).StatusCode);
    }

    [Fact]
    public async Task Forgot_password_gives_the_same_response_for_unknown_and_known_emails()
    {
        var user = await factory.CreateUserAsync(Roles.Staff);
        var unknown = "ninguem." + Guid.NewGuid().ToString("N") + "@teste.local";

        var known = await factory.CreateBrowser().PostFormAsync("/Identity/Account/ForgotPassword", new() { ["Input.Email"] = user.Email! });
        var notKnown = await factory.CreateBrowser().PostFormAsync("/Identity/Account/ForgotPassword", new() { ["Input.Email"] = unknown });

        Assert.Equal(known.StatusCode, notKnown.StatusCode);
        Assert.Equal(known.Headers.Location, notKnown.Headers.Location);
        Assert.Equal(0, factory.Emails.CountTo(unknown));
    }

    [Fact]
    public async Task Resend_confirmation_gives_the_same_response_for_unknown_and_unconfirmed_emails()
    {
        var user = await factory.CreateUserAsync(Roles.Staff, confirmed: false);

        var known = await factory.CreateBrowser().PostFormAsync("/Identity/Account/ResendEmailConfirmation", new() { ["Input.Email"] = user.Email! });
        var unknown = await factory.CreateBrowser().PostFormAsync("/Identity/Account/ResendEmailConfirmation", new() { ["Input.Email"] = NewEmail() });

        Assert.Equal(await Body(known), await Body(unknown));
        Assert.Equal(1, factory.Emails.CountTo(user.Email!));

        static async Task<string> Body(HttpResponseMessage r) =>
            System.Text.RegularExpressions.Regex.Replace(await r.Content.ReadAsStringAsync(), "value=\"[^\"]*\"", "");
    }

    [Fact]
    public async Task Change_password_works_for_a_normal_user()
    {
        var user = await factory.CreateUserAsync(Roles.Staff);
        var client = await factory.CreateBrowser().LoggedInAsync(user.Email!);

        var response = await client.PostFormAsync("/Identity/Account/Manage/ChangePassword", new()
        {
            ["Input.OldPassword"] = MoinhoFactory.Password,
            ["Input.NewPassword"] = "Trocada_789!",
            ["Input.ConfirmPassword"] = "Trocada_789!",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await factory.CreateBrowser().LoginAsync(user.Email!, "Trocada_789!")).StatusCode);
        // This browser stays signed in (its cookie was refreshed with the new security stamp).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Produtos")).StatusCode);
    }

    [Fact]
    public async Task Protected_demo_account_cannot_change_password_or_email()
    {
        // Demo accounts have no password; give this one a password so the test can sign in with the form.
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var demo = await users.FindByEmailAsync(DemoAccounts.StaffEmail);
            await users.AddPasswordAsync(demo!, MoinhoFactory.Password);
        }
        var client = await factory.CreateBrowser().LoggedInAsync(DemoAccounts.StaffEmail);

        await client.PostFormAsync("/Identity/Account/Manage/ChangePassword", new()
        {
            ["Input.OldPassword"] = MoinhoFactory.Password,
            ["Input.NewPassword"] = "Hackeada_123!",
            ["Input.ConfirmPassword"] = "Hackeada_123!",
        });
        var newEmail = NewEmail();
        await client.PostFormAsync("/Identity/Account/Manage/Email", new() { ["Input.NewEmail"] = newEmail },
            postUrl: "/Identity/Account/Manage/Email?handler=ChangeEmail");

        Assert.Equal(HttpStatusCode.Redirect, (await factory.CreateBrowser().LoginAsync(DemoAccounts.StaffEmail, MoinhoFactory.Password)).StatusCode);
        Assert.Equal(0, factory.Emails.CountTo(newEmail));
        Assert.Equal(DemoAccounts.StaffEmail, (await FindAsync(DemoAccounts.StaffEmail))!.Email);
    }

    [Fact]
    public async Task Change_email_takes_effect_only_after_the_link_to_the_new_address_is_opened()
    {
        var user = await factory.CreateUserAsync(Roles.Staff);
        var client = await factory.CreateBrowser().LoggedInAsync(user.Email!);
        var newEmail = NewEmail();

        await client.PostFormAsync("/Identity/Account/Manage/Email", new() { ["Input.NewEmail"] = newEmail },
            postUrl: "/Identity/Account/Manage/Email?handler=ChangeEmail");
        Assert.NotNull(await FindAsync(user.Email!));

        var confirm = await client.GetAsync(factory.Emails.LastTo(newEmail).Link);

        Assert.Contains("Email alterado com sucesso", await confirm.Content.ReadAsStringAsync());
        Assert.Null(await FindAsync(user.Email!));
        Assert.Equal(newEmail, (await FindAsync(newEmail))!.UserName);
    }

    [Fact]
    public async Task Email_failure_shows_a_friendly_message_instead_of_an_error_page()
    {
        factory.Emails.Fail = true;
        try
        {
            var client = factory.CreateClient(); // follows redirects
            var response = await RegisterAsync(client, NewEmail());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("não conseguimos enviar o email de confirmação", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            factory.Emails.Fail = false;
        }
    }

    [Fact]
    public async Task Email_links_use_the_forwarded_host_and_scheme()
    {
        var email = NewEmail();
        var client = factory.CreateBrowser();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "moinho.exemplo.com.br");

        await RegisterAsync(client, email);

        Assert.StartsWith("https://moinho.exemplo.com.br/Identity/Account/ConfirmEmail?", factory.Emails.LastTo(email).Link);
    }

    [Theory]
    [InlineData("/Identity/Account/AccessDenied")]
    [InlineData("/Identity/Account/ExternalLogin")]
    [InlineData("/Identity/Account/LoginWith2fa")]
    [InlineData("/Identity/Account/LoginWithRecoveryCode")]
    [InlineData("/Identity/Account/Manage/SetPassword")]
    [InlineData("/Identity/Account/Manage/ExternalLogins")]
    [InlineData("/Identity/Account/Manage/PersonalData")]
    [InlineData("/Identity/Account/Manage/DownloadPersonalData")]
    [InlineData("/Identity/Account/Manage/DeletePersonalData")]
    [InlineData("/Identity/Account/Manage/TwoFactorAuthentication")]
    [InlineData("/Identity/Account/Manage/EnableAuthenticator")]
    [InlineData("/Identity/Account/Manage/ResetAuthenticator")]
    [InlineData("/Identity/Account/Manage/GenerateRecoveryCodes")]
    [InlineData("/Identity/Account/Manage/ShowRecoveryCodes")]
    [InlineData("/Identity/Account/Manage/Disable2fa")]
    [InlineData("/Identity/Error")]
    public async Task Unused_Identity_UI_pages_return_404(string path)
    {
        var client = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/Identity/Account/Manage")]
    [InlineData("/Identity/Account/Manage/Email")]
    [InlineData("/Identity/Account/Manage/ChangePassword")]
    public async Task Used_Manage_pages_are_served(string path)
    {
        var client = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }
}

public class LowRateLimitFactory(SqlServerFixture sql) : MoinhoFactory(sql)
{
    protected override int RateLimit => 2;
}

public class RateLimitTests(LowRateLimitFactory factory) : IClassFixture<LowRateLimitFactory>
{
    [Fact]
    public async Task Email_sending_posts_are_rate_limited_but_page_views_are_not()
    {
        var client = factory.CreateBrowser();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            var r = await client.PostFormAsync("/Identity/Account/ForgotPassword", new() { ["Input.Email"] = "x@teste.local" });
            statuses.Add(r.StatusCode);
        }

        Assert.Equal([HttpStatusCode.Redirect, HttpStatusCode.Redirect, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Identity/Account/ForgotPassword")).StatusCode);
    }
}
