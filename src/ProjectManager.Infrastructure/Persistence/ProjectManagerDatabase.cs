using Microsoft.EntityFrameworkCore;

namespace ProjectManager.Infrastructure.Persistence;

/// <summary>
/// Configuração única do provedor de banco, compartilhada pela aplicação, pelas ferramentas de
/// migration e pelos testes.
/// </summary>
public static class ProjectManagerDatabase
{
    public const string ConnectionStringName = "ProjectManager";

    public const string HealthCheckName = "database";

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();
    }
}
