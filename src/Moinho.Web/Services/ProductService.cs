using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Moinho.Web.Data;
using Moinho.Web.Models;
using Moinho.Web.ViewModels;

namespace Moinho.Web.Services;

public class ProductService(AppDbContext db, TimeProvider time)
{
    public const string DuplicateSkuMessage = "SKU já cadastrado.";

    public async Task<PagedList<Product>> ListAsync(ProductListQuery query)
    {
        var products = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            products = products.Where(p => p.Name.Contains(term) || p.Sku.Contains(term));
        }

        products = query.Status switch
        {
            "active" => products.Where(p => p.IsActive),
            "inactive" => products.Where(p => !p.IsActive),
            _ => products,
        };

        var descending = query.Dir == "desc";
        var ordered = query.Sort switch
        {
            "price" => descending ? products.OrderByDescending(p => p.Price) : products.OrderBy(p => p.Price),
            "date" => descending ? products.OrderByDescending(p => p.DateAdded) : products.OrderBy(p => p.DateAdded),
            _ => descending ? products.OrderByDescending(p => p.Name) : products.OrderBy(p => p.Name),
        };

        var total = await products.CountAsync();
        var page = Math.Max(1, query.Page);
        var items = await ordered
            .ThenBy(p => p.Id) // stable order across pages
            .Skip((page - 1) * ProductListQuery.PageSize)
            .Take(ProductListQuery.PageSize)
            .ToListAsync();

        return new PagedList<Product>(items, page, ProductListQuery.PageSize, total);
    }

    public Task<Product?> FindAsync(int id) => db.Products.FirstOrDefaultAsync(p => p.Id == id);

    // Returns the new product, or null when the SKU is already taken.
    public async Task<Product?> CreateAsync(ProductFormViewModel form)
    {
        var product = new Product { DateAdded = time.GetUtcNow().UtcDateTime };
        form.ApplyTo(product);
        db.Products.Add(product);
        return await TrySaveAsync() ? product : null;
    }

    // Returns false when the new SKU is already taken. Last write wins: no concurrency check.
    public async Task<bool> UpdateAsync(Product product, ProductFormViewModel form)
    {
        form.ApplyTo(product);
        product.UpdatedAt = time.GetUtcNow().UtcDateTime;
        return await TrySaveAsync();
    }

    public async Task DeleteAsync(Product product)
    {
        db.Products.Remove(product);
        await db.SaveChangesAsync();
    }

    // The unique index on Sku is the real guard (it also covers two people saving at the same time).
    // We let the database reject the duplicate and turn its error into a form message.
    private async Task<bool> TrySaveAsync()
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            return false;
        }
    }
}
