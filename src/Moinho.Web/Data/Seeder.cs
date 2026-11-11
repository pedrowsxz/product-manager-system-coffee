using System.Globalization;
using System.Text;
using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moinho.Web.Models;

namespace Moinho.Web.Data;

// Idempotent: running it again only adds what is missing. Deterministic: the fixed Bogus seed
// makes every run (and every nightly reset) produce the same staff and products.
public class Seeder(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<SeedOptions> seedOptions,
    TimeProvider time)
{
    public const int StaffCount = 10;
    public const int ProductCount = 60;

    public async Task SeedAsync()
    {
        Randomizer.Seed = new Random(2468);
        var faker = new Faker("pt_BR");
        var now = time.GetUtcNow().UtcDateTime;

        // Generate everything up front so the random sequence is the same whether or not rows exist.
        var staff = BuildStaff(faker, now);
        var products = BuildProducts(faker, now);

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                Check(await roleManager.CreateAsync(new IdentityRole(role)));
            }
        }

        var seed = seedOptions.Value;
        if (string.IsNullOrWhiteSpace(seed.AdminEmail) || string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            throw new InvalidOperationException("Configure Seed:AdminEmail e Seed:AdminPassword (veja .env.example).");
        }

        await EnsureUserAsync(new ApplicationUser { FullName = seed.AdminName, Email = seed.AdminEmail, EmailConfirmed = true, CreatedAt = now }, Roles.Admin, password: seed.AdminPassword);

        // Demo accounts have no password: the only way in is the demo button on the login page.
        await EnsureUserAsync(new ApplicationUser { FullName = "Admin Demonstração", Email = DemoAccounts.AdminEmail, EmailConfirmed = true, CreatedAt = now, IsProtectedDemo = true }, Roles.Admin);
        await EnsureUserAsync(new ApplicationUser { FullName = "Equipe Demonstração", Email = DemoAccounts.StaffEmail, EmailConfirmed = true, CreatedAt = now, IsProtectedDemo = true }, Roles.Staff);

        // Filler accounts: a random password nobody knows, so they look like normal staff in the list.
        foreach (var (user, role, inactive) in staff)
        {
            await EnsureUserAsync(user, role, password: $"{Guid.NewGuid():N}Aa1!", inactive: inactive);
        }

        if (!await db.Products.AnyAsync())
        {
            db.Products.AddRange(products);
            await db.SaveChangesAsync();
        }
    }

    private async Task EnsureUserAsync(ApplicationUser user, string role, string? password = null, bool inactive = false)
    {
        if (await userManager.FindByEmailAsync(user.Email!) is not null)
        {
            return;
        }

        user.UserName = user.Email;

        Check(password is null ? await userManager.CreateAsync(user) : await userManager.CreateAsync(user, password));
        Check(await userManager.AddToRoleAsync(user, role));

        if (inactive)
        {
            Check(await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue));
        }
    }

    private static List<(ApplicationUser User, string Role, bool Inactive)> BuildStaff(Faker faker, DateTime now)
    {
        var result = new List<(ApplicationUser, string, bool)>();
        var usedEmails = new HashSet<string>();

        while (result.Count < StaffCount)
        {
            var first = faker.Name.FirstName();
            var last = faker.Name.LastName();
            var email = $"{Slug(first)}.{Slug(last)}@moinho.app";
            if (!usedEmails.Add(email))
            {
                continue;
            }

            var index = result.Count;
            var user = new ApplicationUser
            {
                FullName = $"{first} {last}",
                Email = email,
                CreatedAt = now.AddDays(-faker.Random.Int(10, 360)),
                // One unconfirmed account so the Admin "Não confirmado" filter has something to show.
                EmailConfirmed = index != 7,
            };
            var role = index == 0 ? Roles.Admin : Roles.Staff;
            result.Add((user, role, index == 8));
        }

        return result;
    }

    private static List<Product> BuildProducts(Faker faker, DateTime now)
    {
        (string Name, string Code, decimal PricePerKg)[] origins =
        [
            ("Sul de Minas", "SMG", 150m), ("Cerrado Mineiro", "CER", 140m), ("Mogiana", "MOG", 135m),
            ("Chapada Diamantina", "CHD", 210m), ("Caparaó", "CAP", 190m), ("Mantiqueira de Minas", "MNT", 200m),
        ];
        (string Name, string Code)[] roasts = [("Clara", "CL"), ("Média", "MD"), ("Escura", "ES")];
        (string Name, string Code)[] formats = [("em grãos", "GR"), ("moído", "MO")];
        (int Grams, string Label, string Code)[] weights = [(250, "250 g", "250"), (500, "500 g", "500"), (1000, "1 kg", "1K")];
        string[] notes = ["chocolate e caramelo", "frutas amarelas", "castanhas e mel", "frutas vermelhas", "cacau e especiarias", "rapadura", "cítricos e floral"];
        string[] methods = ["coado", "espresso", "prensa francesa", "moka", "Aeropress"];

        var coffees =
            from o in origins
            from r in roasts
            from f in formats
            from w in weights
            select new { o, r, f, w };

        var products = new List<Product>();

        foreach (var c in faker.PickRandom(coffees, 40))
        {
            // Smaller packs cost more per kg, as in a real store.
            var factor = c.w.Grams switch { 250 => 1.15m, 500 => 1.05m, _ => 1m };
            products.Add(new Product
            {
                Name = $"Café {c.o.Name} Torra {c.r.Name} {c.f.Name} {c.w.Label}",
                Sku = $"CAF-{c.o.Code}-{c.r.Code}-{c.f.Code}-{c.w.Code}",
                Description = $"Notas de {faker.PickRandom(notes)}. Ótimo para {faker.PickRandom(methods)}.",
                Price = EndIn90(c.o.PricePerKg * c.w.Grams / 1000m * factor),
            });
        }

        foreach (var c in faker.PickRandom(from o in origins from r in roasts select new { o, r }, 8))
        {
            products.Add(new Product
            {
                Name = $"Cápsulas {c.o.Name} Torra {c.r.Name} (10 un.)",
                Sku = $"CPS-{c.o.Code}-{c.r.Code}-10",
                Description = "Compatíveis com máquinas Nespresso®. Café 100% arábica, moído na hora do envase.",
                Price = EndIn90(c.o.PricePerKg / 6m),
            });
        }

        (string Name, string Sku, decimal Price, string Description)[] accessories =
        [
            ("Moedor manual com mó de cerâmica", "ACS-MOEDOR-CER", 189.90m, "Regulagem de moagem do espresso ao french press."),
            ("Prensa francesa 600 ml", "ACS-PRENSA-600", 129.90m, "Vidro borossilicato e filtro de aço inox."),
            ("Coador V60 de cerâmica tamanho 02", "ACS-V60-CER-02", 149.90m, "Para 1 a 4 xícaras."),
            ("Filtros de papel V60 02 (100 un.)", "ACS-FILTRO-V60", 34.90m, "Papel branco, sem gosto residual."),
            ("Balança digital com timer", "ACS-BALANCA", 169.90m, "Precisão de 0,1 g, ideal para receitas de coado."),
            ("Chaleira bico de ganso 1 L", "ACS-CHALEIRA-1L", 249.90m, "Controle fino do fluxo de água."),
            ("Caneca esmaltada 350 ml", "ACS-CANECA-350", 49.90m, "Esmalte vitrificado, vai à lava-louças."),
            ("Kit barista: pitcher e tamper", "ACS-KIT-BARISTA", 219.90m, "Pitcher de 350 ml e tamper de 58 mm."),
            ("Coador de pano (3 un.)", "ACS-PANO-3", 24.90m, "Algodão cru, tamanho médio."),
            ("Cafeteira italiana 6 xícaras", "ACS-MOKA-6", 159.90m, "Alumínio, para fogão a gás e elétrico."),
            ("Suporte de madeira para coador", "ACS-SUPORTE-MAD", 89.90m, "Madeira de reflorestamento."),
            ("Lata hermética 500 g", "ACS-LATA-500", 79.90m, "Válvula de CO₂ para manter o frescor dos grãos."),
        ];

        foreach (var a in accessories)
        {
            products.Add(new Product { Name = a.Name, Sku = a.Sku, Price = a.Price, Description = a.Description });
        }

        foreach (var product in products)
        {
            product.IsActive = faker.Random.Bool(0.85f);
            product.StockQuantity = faker.Random.Bool(0.1f) ? 0 : faker.Random.Int(1, 120);
            product.DateAdded = now.AddDays(-faker.Random.Int(0, 364)).AddMinutes(-faker.Random.Int(0, 1440));
            product.UpdatedAt = faker.Random.Bool(0.3f) ? product.DateAdded.AddDays(faker.Random.Int(1, 20)) : null;
            if (product.UpdatedAt > now)
            {
                product.UpdatedAt = null;
            }
        }

        return products;
    }

    private static decimal EndIn90(decimal price) => Math.Floor(price) + 0.90m;

    private static string Slug(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var chars = normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetter(c));
        return new string(chars.ToArray()).ToLowerInvariant();
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Seed falhou: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
