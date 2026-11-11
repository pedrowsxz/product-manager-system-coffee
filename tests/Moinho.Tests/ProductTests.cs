using System.Net;
using Microsoft.EntityFrameworkCore;
using Moinho.Tests.Infrastructure;
using Moinho.Web.Models;

namespace Moinho.Tests;

public class ProductTests(MoinhoFactory factory) : IClassFixture<MoinhoFactory>
{
    private async Task<HttpClient> StaffAsync()
    {
        var user = await factory.CreateUserAsync(Roles.Staff);
        return await factory.CreateBrowser().LoggedInAsync(user.Email!);
    }

    private static string NewSku() => "TST-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static Dictionary<string, string> Form(string sku, string price = "10,00", string stock = "5") => new()
    {
        ["Name"] = "Café de teste",
        ["Sku"] = sku,
        ["Price"] = price,
        ["StockQuantity"] = stock,
        ["IsActive"] = "true",
    };

    private Task<Product?> FindBySkuAsync(string sku) =>
        factory.WithDbAsync(db => db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Sku == sku));

    [Theory]
    [InlineData("12,50", 12.50)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234,5", 1234.50)]
    [InlineData("7", 7)]
    public async Task Prices_typed_the_Brazilian_way_are_saved_correctly(string typed, double expected)
    {
        var client = await StaffAsync();
        var sku = NewSku();

        var response = await client.PostFormAsync("/Produtos/Novo", Form(sku, typed));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal((decimal)expected, (await FindBySkuAsync(sku))!.Price);
    }

    [Theory]
    [InlineData("12.50")]
    [InlineData("12,505")]
    [InlineData("abc")]
    public async Task Prices_with_a_dot_for_cents_are_rejected_not_misread(string typed)
    {
        var client = await StaffAsync();
        var sku = NewSku();

        var response = await client.PostFormAsync("/Produtos/Novo", Form(sku, typed));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Use o formato brasileiro, com vírgula nos centavos", await response.Content.ReadAsStringAsync());
        Assert.Null(await FindBySkuAsync(sku));
    }

    [Theory]
    [InlineData("0,00", "5", "O preço deve ser maior que zero.")]
    [InlineData("10,00", "-1", "O estoque não pode ser negativo.")]
    public async Task Zero_price_and_negative_stock_are_rejected(string price, string stock, string message)
    {
        var client = await StaffAsync();
        var sku = NewSku();

        var response = await client.PostFormAsync("/Produtos/Novo", Form(sku, price, stock));

        Assert.Contains(message, await response.Content.ReadAsStringAsync());
        Assert.Null(await FindBySkuAsync(sku));
    }

    [Fact]
    public async Task Sku_is_stored_in_uppercase()
    {
        var client = await StaffAsync();
        var sku = NewSku();

        await client.PostFormAsync("/Produtos/Novo", Form(sku.ToLowerInvariant()));

        Assert.NotNull(await FindBySkuAsync(sku));
    }

    [Fact]
    public async Task Duplicate_sku_is_a_field_error()
    {
        var client = await StaffAsync();
        var existing = await factory.WithDbAsync(db => db.Products.Select(p => p.Sku).FirstAsync());

        var response = await client.PostFormAsync("/Produtos/Novo", Form(existing.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("SKU já cadastrado.", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await factory.WithDbAsync(db => db.Products.CountAsync(p => p.Sku == existing)));
    }

    [Fact]
    public async Task Database_itself_rejects_a_duplicate_sku()
    {
        var existing = await factory.WithDbAsync(db => db.Products.Select(p => p.Sku).FirstAsync());

        await Assert.ThrowsAsync<DbUpdateException>(() => factory.WithDbAsync(db =>
        {
            db.Products.Add(new Product { Name = "Duplicado", Sku = existing, Price = 1, DateAdded = DateTime.UtcNow });
            return db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task DateAdded_cannot_be_over_posted_on_create_or_edit()
    {
        var client = await StaffAsync();
        var sku = NewSku();
        var form = Form(sku);
        form["DateAdded"] = "2001-01-01";
        form["Id"] = "999999";

        await client.PostFormAsync("/Produtos/Novo", form);
        var created = (await FindBySkuAsync(sku))!;
        Assert.True(created.DateAdded > DateTime.UtcNow.AddMinutes(-5));

        var edit = Form(sku, "20,00");
        edit["DateAdded"] = "2001-01-01";
        var response = await client.PostFormAsync($"/Produtos/{created.Id}/Editar", edit);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var edited = (await FindBySkuAsync(sku))!;
        Assert.Equal(created.DateAdded, edited.DateAdded);
        Assert.Equal(20m, edited.Price);
        Assert.NotNull(edited.UpdatedAt);
    }

    [Fact]
    public async Task Edit_to_an_existing_sku_is_a_field_error()
    {
        var client = await StaffAsync();
        var skus = await factory.WithDbAsync(db => db.Products.OrderBy(p => p.Id).Select(p => new { p.Id, p.Sku }).Take(2).ToListAsync());

        var response = await client.PostFormAsync($"/Produtos/{skus[0].Id}/Editar", Form(skus[1].Sku));

        Assert.Contains("SKU já cadastrado.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Delete_asks_for_confirmation_then_removes_the_product()
    {
        var client = await StaffAsync();
        var sku = NewSku();
        await client.PostFormAsync("/Produtos/Novo", Form(sku));
        var id = (await FindBySkuAsync(sku))!.Id;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Produtos/{id}/Excluir")).StatusCode);
        Assert.NotNull(await FindBySkuAsync(sku));

        var response = await client.PostFormAsync($"/Produtos/{id}/Excluir", new());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await FindBySkuAsync(sku));
    }

    [Fact]
    public async Task List_searches_filters_sorts_and_pages()
    {
        var client = await StaffAsync();

        var search = await (await client.GetAsync("/Produtos?q=ACS-")).Content.ReadAsStringAsync();
        Assert.Contains("ACS-MOEDOR-CER", search);
        Assert.DoesNotContain("CAF-", search);

        var inactive = await (await client.GetAsync("/Produtos?status=inactive")).Content.ReadAsStringAsync();
        var inactiveCount = await factory.WithDbAsync(db => db.Products.CountAsync(p => !p.IsActive));
        Assert.Contains($"{inactiveCount} registro(s)", inactive);
        Assert.DoesNotContain("badge-active", inactive);

        var cheapest = await factory.WithDbAsync(db => db.Products.OrderBy(p => p.Price).ThenBy(p => p.Id).Select(p => p.Sku).FirstAsync());
        var byPrice = await (await client.GetAsync("/Produtos?sort=price&dir=asc")).Content.ReadAsStringAsync();
        Assert.Equal(cheapest, System.Text.RegularExpressions.Regex.Match(byPrice, "<td class=\"sku\">([^<]+)</td>").Groups[1].Value);

        var page3 = await (await client.GetAsync("/Produtos?page=3")).Content.ReadAsStringAsync();
        Assert.Contains("página 3 de", page3);
        Assert.Equal(20, System.Text.RegularExpressions.Regex.Matches(await (await client.GetAsync("/Produtos?page=1")).Content.ReadAsStringAsync(), "<td class=\"sku\">").Count);
    }
}
