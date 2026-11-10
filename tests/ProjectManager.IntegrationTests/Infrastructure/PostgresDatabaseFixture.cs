using Microsoft.EntityFrameworkCore;
using ProjectManager.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(ProjectManager.IntegrationTests.Infrastructure.PostgresDatabaseFixture))]

namespace ProjectManager.IntegrationTests.Infrastructure;

/// <summary>
/// PostgreSQL real e descartável (Testcontainers), compartilhado pela suíte, com as migrations aplicadas.
/// Requer Docker em execução. Cada teste cria as próprias organizações, isolando seus dados.
/// </summary>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:18-alpine";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage).Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = CreateContext(organizationId: null);
        await context.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public ProjectManagerDbContext CreateContext(Guid? organizationId, TimeProvider? timeProvider = null)
    {
        var options = new DbContextOptionsBuilder<ProjectManagerDbContext>();
        ProjectManagerDatabase.Configure(options, ConnectionString);

        return new ProjectManagerDbContext(
            options.Options,
            new TestTenantContext(organizationId),
            timeProvider ?? TimeProvider.System);
    }
}
