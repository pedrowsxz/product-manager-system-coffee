using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(Moinho.Tests.Infrastructure.SqlServerFixture))]

namespace Moinho.Tests.Infrastructure;

// One SQL Server container for the whole test run; each test class gets its own database in it.
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionStringFor(string database) =>
        new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database }.ConnectionString;

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
