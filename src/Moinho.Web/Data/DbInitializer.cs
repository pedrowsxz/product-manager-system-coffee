using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Moinho.Web.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbInitializer));

        // The brief mandates EnsureCreated: it builds every table (Identity, Products, ErrorLogs) straight
        // from the EF model when the database doesn't exist yet. It never alters an existing schema and
        // keeps no migration history, so a model change means dropping the database (see docs/adr/0002).
        // In Docker the app can start before SQL Server accepts logins, so retry for about a minute.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
                break;
            }
            catch (SqlException ex) when (attempt < 30)
            {
                logger.LogWarning("SQL Server ainda não está pronto (tentativa {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync();
    }
}
