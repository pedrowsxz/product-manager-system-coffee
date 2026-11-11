using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moinho.Tests.Infrastructure;
using Moinho.Web.Areas.Admin.Controllers;
using Moinho.Web.Models;

namespace Moinho.Tests;

public class AdminAuthorizationTests(MoinhoFactory factory) : IClassFixture<MoinhoFactory>
{
    public static TheoryData<string> AdminPages => ["/Admin", "/Admin/Equipe", "/Admin/Equipe/Novo"];

    [Fact]
    public void Every_Admin_area_controller_requires_the_Admin_policy()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .Where(t => t.Namespace!.StartsWith("Moinho.Web.Areas.Admin") || t.GetCustomAttribute<AreaAttribute>(inherit: true)?.RouteValue == "Admin")
            .ToList();

        Assert.NotEmpty(controllers);
        foreach (var controller in controllers)
        {
            Assert.True(typeof(AdminControllerBase).IsAssignableFrom(controller), $"{controller.Name} deve herdar de AdminControllerBase");
            Assert.Contains(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true), a => a.Policy == Policies.RequireAdmin);
            Assert.Equal("Admin", controller.GetCustomAttribute<AreaAttribute>(inherit: true)?.RouteValue);

            // [AllowAnonymous] anywhere would silently override the policy.
            Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true));
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            }
        }
    }

    [Theory]
    [MemberData(nameof(AdminPages))]
    public async Task Staff_gets_403_on_Admin_pages(string path)
    {
        var user = await factory.CreateUserAsync(Roles.Staff);
        var client = await factory.CreateBrowser().LoggedInAsync(user.Email!);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Acesso negado", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [MemberData(nameof(AdminPages))]
    public async Task Admin_gets_200_on_Admin_pages(string path)
    {
        var client = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_sent_to_login_from_Admin()
    {
        var response = await factory.CreateBrowser().GetAsync("/Admin/Equipe");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Admin_link_is_only_shown_to_admins()
    {
        var staff = await factory.CreateUserAsync(Roles.Staff);
        var staffHtml = await (await (await factory.CreateBrowser().LoggedInAsync(staff.Email!)).GetAsync("/Produtos")).Content.ReadAsStringAsync();
        var adminHtml = await (await (await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword)).GetAsync("/Produtos")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("href=\"/Admin/Equipe\"", staffHtml);
        Assert.Contains("href=\"/Admin/Equipe\"", adminHtml);
    }

    [Fact]
    public async Task Admin_posts_require_an_antiforgery_token()
    {
        var client = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);

        var response = await client.PostAsync("/Admin/Equipe/Novo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Sem Token", ["Email"] = "sem.token@teste.local", ["Role"] = "Staff",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
