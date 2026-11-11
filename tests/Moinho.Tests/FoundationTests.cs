using System.Net;
using Microsoft.EntityFrameworkCore;
using Moinho.Tests.Infrastructure;
using Moinho.Web.Data;

namespace Moinho.Tests;

public class FoundationTests(MoinhoFactory factory) : IClassFixture<MoinhoFactory>
{
    [Fact]
    public async Task Seeder_creates_roles_users_and_products()
    {
        var (roles, users, products) = await factory.WithDbAsync(async db =>
            (await db.Roles.CountAsync(), await db.Users.CountAsync(), await db.Products.CountAsync()));

        Assert.Equal(2, roles);
        Assert.Equal(3 + Seeder.StaffCount, users);
        Assert.Equal(Seeder.ProductCount, products);
    }

    [Fact]
    public async Task Anonymous_products_page_redirects_to_login()
    {
        var response = await factory.CreateBrowser().GetAsync("/Produtos");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Static_files_and_login_page_are_public()
    {
        var client = factory.CreateBrowser();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/css/site.css")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Identity/Account/Login")).StatusCode);
    }

    [Fact]
    public async Task Admin_can_sign_in_and_see_products()
    {
        var client = await factory.CreateBrowser().LoggedInAsync(MoinhoFactory.AdminEmail, MoinhoFactory.AdminPassword);

        var response = await client.GetAsync("/Produtos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
