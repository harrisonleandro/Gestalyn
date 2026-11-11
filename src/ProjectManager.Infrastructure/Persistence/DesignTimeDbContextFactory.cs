using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ProjectManager.Infrastructure.MultiTenancy;

namespace ProjectManager.Infrastructure.Persistence;

/// <summary>
/// Usada somente pelas ferramentas do EF Core (<c>dotnet ef</c>). A geração de migrations não abre
/// conexão; para aplicar migrations, informe a conexão com <c>--connection</c> ou pela variável de
/// ambiente <c>ConnectionStrings__ProjectManager</c>.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProjectManagerDbContext>
{
    private const string PlaceholderConnectionString = "Host=localhost;Database=projectmanager";

    public ProjectManagerDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable($"ConnectionStrings__{ProjectManagerDatabase.ConnectionStringName}")
            ?? PlaceholderConnectionString;

        var options = new DbContextOptionsBuilder<ProjectManagerDbContext>();
        ProjectManagerDatabase.Configure(options, connectionString);

        return new ProjectManagerDbContext(options.Options, new UnresolvedTenantContext(), TimeProvider.System);
    }
}
