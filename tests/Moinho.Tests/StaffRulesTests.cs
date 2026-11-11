using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moinho.Tests.Infrastructure;
using Moinho.Web.Data;
using Moinho.Web.Models;

namespace Moinho.Tests;

public class StaffRulesTests(MoinhoFactory factory) : IClassFixture<MoinhoFactory>
{
    private async Task<ApplicationUser> ReloadAsync(string id)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(id))!;
    }

    private async Task<IList<string>> RolesOfAsync(string id)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetRolesAsync((await users.FindByIdAsync(id))!);
    }

    private async Task<string> IdOfAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }

    private static Dictionary<string, string> EditForm(ApplicationUser user, string role, bool active = true, string? email = null) => new()
    {
        ["FullName"] = user.FullName,
        ["Email"] = email ?? user.Email!,
        ["Role"] = role,
        ["IsActive"] = active ? "true" : "false",
    };

    private static Task<HttpResponseMessage> EditAsync(HttpClient client, string id, Dictionary<string, string> form) =>
        client.PostFormAsync($"/Admin/Equipe/{id}/Editar", form);

    [Fact]
    public async Task Admin_cannot_demote_deactivate_or_delete_themselves()
    {
        var admin = await factory.CreateUserAsync(Roles.Admin);
        var client = await factory.CreateBrowser().LoggedInAsync(admin.Email!);

        var demote = await EditAsync(client, admin.Id, EditForm(admin, Roles.Staff));
        Assert.Contains("Você não pode remover seu próprio perfil de administrador.", await demote.Content.ReadAsStringAsync());

        var deactivate = await EditAsync(client, admin.Id, EditForm(admin, Roles.Admin, active: false));
        Assert.Contains("Você não pode desativar a própria conta.", await deactivate.Content.ReadAsStringAsync());

        await client.PostFormAsync($"/Admin/Equipe/{admin.Id}/Excluir", new());

        var reloaded = await ReloadAsync(admin.Id);
        Assert.NotNull(reloaded);
        Assert.Null(reloaded.LockoutEnd);
        Assert.Equal([Roles.Admin], await RolesOfAsync(admin.Id));
    }

    [Theory]
    [InlineData(DemoAccounts.AdminEmail)]
    [InlineData(MoinhoFactory.AdminEmail)] // the real admin from configuration
    public async Task Protected_accounts_cannot_be_demoted_deactivated_or_deleted(string email)
    {
        var admin = await factory.CreateUserAsync(Roles.Admin);
        var client = await factory.CreateBrowser().LoggedInAsync(admin.Email!);
        var id = await IdOfAsync(email);
        var target = await ReloadAsync(id);

        var demote = await EditAsync(client, id, EditForm(target, Roles.Staff));
        Assert.Contains("Esta conta é protegida", await demote.Content.ReadAsStringAsync());
        await EditAsync(client, id, EditForm(target, Roles.Admin, active: false));
        await client.PostFormAsync($"/Admin/Equipe/{id}/Excluir", new());

        var reloaded = await ReloadAsync(id);
        Assert.NotNull(reloaded);
        Assert.Null(reloaded.LockoutEnd);
        Assert.Equal([Roles.Admin], await RolesOfAsync(id));
    }

    [Fact]
    public async Task Protected_demo_account_email_cannot_be_changed_by_an_admin()
    {
        var admin = await factory.CreateUserAsync(Roles.Admin);
        var client = await factory.CreateBrowser().LoggedInAsync(admin.Email!);
        var id = await IdOfAsync(DemoAccounts.StaffEmail);

        var response = await EditAsync(client, id, EditForm(await ReloadAsync(id), Roles.Staff, email: "outro@teste.local"));

        Assert.Contains("Contas de demonstração não podem trocar de email.", await response.Content.ReadAsStringAsync());
        Assert.Equal(DemoAccounts.StaffEmail, (await ReloadAsync(id)).Email);
    }

    [Fact]
    public async Task Invited_user_has_no_password_and_gets_a_set_password_link()
    {
        var client = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);
        var email = $"convidado.{Guid.NewGuid():N}@teste.local";

        var response = await client.PostFormAsync("/Admin/Equipe/Novo", new() { ["FullName"] = "Pessoa Convidada", ["Email"] = email, ["Role"] = Roles.Staff });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var user = await ReloadAsync(await IdOfAsync(email));
        Assert.Null(user.PasswordHash);
        Assert.True(user.EmailConfirmed);
        Assert.Equal([Roles.Staff], await RolesOfAsync(user.Id));

        var invite = factory.Emails.LastTo(email);
        Assert.Contains("convidado", invite.Subject);
        Assert.Contains("invite=True", invite.Link);

        var browser = factory.CreateBrowser();
        var page = await (await browser.GetAsync(invite.Link)).Content.ReadAsStringAsync();
        Assert.Contains("Definir sua senha", page);
        Assert.Contains($"value=\"{email}\"", page); // the email comes pre-filled
        var set = await browser.PostFormAsync(invite.Link, new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = "Minha_Senha_1!",
            ["Input.ConfirmPassword"] = "Minha_Senha_1!",
        }, postUrl: "/Identity/Account/ResetPassword");
        Assert.Equal("/Identity/Account/ResetPasswordConfirmation", set.Headers.Location!.OriginalString);
        await factory.CreateBrowser().LoggedInAsync(email, "Minha_Senha_1!");
    }

    [Fact]
    public async Task Inviting_an_existing_email_is_a_field_error()
    {
        var client = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);

        var response = await client.PostFormAsync("/Admin/Equipe/Novo", new() { ["FullName"] = "Repetido", ["Email"] = DemoAccounts.StaffEmail, ["Role"] = Roles.Staff });

        Assert.Contains("Já existe uma conta com este email.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Demoted_admin_loses_Admin_access_on_their_next_request()
    {
        var victim = await factory.CreateUserAsync(Roles.Admin);
        var victimClient = await factory.CreateBrowser().LoggedInAsync(victim.Email!);
        Assert.Equal(HttpStatusCode.OK, (await victimClient.GetAsync("/Admin")).StatusCode);

        var admin = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);
        var result = await EditAsync(admin, victim.Id, EditForm(victim, Roles.Staff));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);

        // Same cookie as before: the security stamp no longer matches, so the user is signed out.
        var next = await victimClient.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, next.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", next.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Deactivated_user_is_signed_out_and_cannot_sign_in_until_reactivated()
    {
        var user = await factory.CreateUserAsync(Roles.Staff);
        var userClient = await factory.CreateBrowser().LoggedInAsync(user.Email!);
        var admin = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);

        await EditAsync(admin, user.Id, EditForm(user, Roles.Staff, active: false));

        Assert.Equal(HttpStatusCode.Redirect, (await userClient.GetAsync("/Produtos")).StatusCode);
        var login = await factory.CreateBrowser().LoginAsync(user.Email!, MoinhoFactory.Password);
        Assert.Equal("/Identity/Account/Lockout", login.Headers.Location!.OriginalString);

        await EditAsync(admin, user.Id, EditForm(user, Roles.Staff, active: true));
        await factory.CreateBrowser().LoggedInAsync(user.Email!);
    }

    [Fact]
    public async Task Changing_a_users_email_requires_confirming_the_new_address()
    {
        var user = await factory.CreateUserAsync(Roles.Staff);
        var admin = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);
        var newEmail = $"trocado.{Guid.NewGuid():N}@teste.local";

        await EditAsync(admin, user.Id, EditForm(user, Roles.Staff, email: newEmail));

        var reloaded = await ReloadAsync(user.Id);
        Assert.Equal(newEmail, reloaded.Email);
        Assert.Equal(newEmail, reloaded.UserName);
        Assert.False(reloaded.EmailConfirmed);
        Assert.Contains("/Identity/Account/ConfirmEmail", factory.Emails.LastTo(newEmail).Link);
    }

    [Fact]
    public async Task Admin_can_delete_a_staff_member_after_confirmation()
    {
        var user = await factory.CreateUserAsync(Roles.Staff);
        var admin = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Admin/Equipe/{user.Id}/Excluir")).StatusCode);
        await admin.PostFormAsync($"/Admin/Equipe/{user.Id}/Excluir", new());

        Assert.Null(await ReloadAsync(user.Id));
    }

    [Fact]
    public async Task Staff_list_filters_by_role_and_status()
    {
        var admin = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);

        var unconfirmed = await (await admin.GetAsync("/Admin/Equipe?status=unconfirmed")).Content.ReadAsStringAsync();
        Assert.Contains("Não confirmado", unconfirmed);
        Assert.DoesNotContain("badge-active", unconfirmed);

        var inactive = await (await admin.GetAsync("/Admin/Equipe?status=inactive")).Content.ReadAsStringAsync();
        Assert.Contains("badge-inactive", inactive);

        var admins = await (await admin.GetAsync("/Admin/Equipe?role=Admin&q=demo")).Content.ReadAsStringAsync();
        Assert.Contains(DemoAccounts.AdminEmail, admins);
        Assert.DoesNotContain(DemoAccounts.StaffEmail, admins);
    }
}

// A database where only one active admin is left, to check the last-admin rule.
public class LastAdminTests(MoinhoFactory factory) : IClassFixture<MoinhoFactory>
{
    [Fact]
    public async Task The_last_active_admin_cannot_be_demoted_deactivated_or_deleted()
    {
        string lastAdminId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            // Leave exactly one active admin: an ordinary (unprotected) one.
            foreach (var other in await users.GetUsersInRoleAsync(Roles.Admin))
            {
                other.IsProtectedDemo = false;
                await users.UpdateAsync(other);
                await users.RemoveFromRoleAsync(other, Roles.Admin);
                await users.AddToRoleAsync(other, Roles.Staff);
            }
            var last = new ApplicationUser { UserName = "ultimo@teste.local", Email = "ultimo@teste.local", FullName = "Último Admin", EmailConfirmed = true };
            await users.CreateAsync(last, MoinhoFactory.Password);
            await users.AddToRoleAsync(last, Roles.Admin);
            lastAdminId = last.Id;
        }

        // Call the service directly, acting as a different admin id, so only the last-admin rule applies.
        using var serviceScope = factory.Services.CreateScope();
        var staff = serviceScope.ServiceProvider.GetRequiredService<Moinho.Web.Services.StaffService>();
        var form = new Moinho.Web.ViewModels.StaffEditViewModel { FullName = "Último Admin", Email = "ultimo@teste.local", Role = Roles.Staff, IsActive = true };

        var demote = await staff.UpdateAsync("outro-admin", lastAdminId, form);
        form.Role = Roles.Admin;
        form.IsActive = false;
        var deactivate = await staff.UpdateAsync("outro-admin", lastAdminId, form);
        var delete = await staff.DeleteAsync("outro-admin", lastAdminId);

        Assert.False(demote.Succeeded);
        Assert.Contains("último administrador ativo", demote.Error);
        Assert.False(deactivate.Succeeded);
        Assert.False(delete.Succeeded);
        Assert.Contains("último administrador ativo", delete.Error);
    }
}
