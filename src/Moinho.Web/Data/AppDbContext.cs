using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Moinho.Web.Models;

namespace Moinho.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.FullName).HasMaxLength(100);
        });

        builder.Entity<Product>(product =>
        {
            product.Property(p => p.Name).HasMaxLength(100);
            product.Property(p => p.Sku).HasMaxLength(20);
            product.HasIndex(p => p.Sku).IsUnique();
            product.Property(p => p.Description).HasMaxLength(1000);
            product.Property(p => p.Price).HasPrecision(10, 2);
        });

        // EnsureCreated creates this table, so the Serilog sink runs with AutoCreateSqlTable = false.
        // Names and types match the sink's standard columns as configured in ErrorLogSink.Columns():
        // Id int identity, Level nvarchar(16), TimeStamp datetime2 (UTC), the rest nvarchar(max).
        builder.Entity<ErrorLog>(log =>
        {
            log.ToTable(ErrorLogSink.TableName);
            log.Property(l => l.Level).HasMaxLength(16);
            log.HasIndex(l => l.TimeStamp);
        });
    }
}
