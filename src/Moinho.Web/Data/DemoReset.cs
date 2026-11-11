using Microsoft.EntityFrameworkCore;

namespace Moinho.Web.Data;

// Puts the public demo back to its seeded state: every row is deleted (children before parents,
// because of the foreign keys) and the deterministic seeder runs again.
// Rows are deleted instead of dropping the database: DROP needs the dbcreator permission and fails
// while other connections are open, which matters when SQL Server is shared with other apps.
// Consequences: self-registered users and logs disappear, and everyone is signed out (their user
// rows no longer exist, so the security-stamp check rejects their cookies).
public class DemoReset(AppDbContext db, Seeder seeder, ILogger<DemoReset> logger)
{
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.ErrorLogs.ExecuteDeleteAsync(cancellationToken);
            await db.Products.ExecuteDeleteAsync(cancellationToken);
            await db.UserTokens.ExecuteDeleteAsync(cancellationToken);
            await db.UserLogins.ExecuteDeleteAsync(cancellationToken);
            await db.UserClaims.ExecuteDeleteAsync(cancellationToken);
            await db.UserRoles.ExecuteDeleteAsync(cancellationToken);
            await db.Users.ExecuteDeleteAsync(cancellationToken);
            await db.RoleClaims.ExecuteDeleteAsync(cancellationToken);
            await db.Roles.ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        db.ChangeTracker.Clear();
        await seeder.SeedAsync();
        logger.LogInformation("Dados de demonstração restaurados");
    }
}
