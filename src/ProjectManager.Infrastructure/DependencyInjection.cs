using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Infrastructure.MultiTenancy;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure;

/// <summary>
/// Ponto de composição da camada de infraestrutura.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Relógio do sistema injetável, substituível em testes por um TimeProvider controlado.
        services.TryAddSingleton(TimeProvider.System);

        // Sem organização ativa até a resolução pela identidade autenticada (Fase 04).
        services.TryAddScoped<ITenantContext, UnresolvedTenantContext>();

        services.AddPersistence();

        return services;
    }

    private static void AddPersistence(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = configuration.GetConnectionString(ProjectManagerDatabase.ConnectionStringName) ?? string.Empty)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                $"A connection string 'ConnectionStrings:{ProjectManagerDatabase.ConnectionStringName}' é obrigatória.")
            .ValidateOnStart();

        services.AddDbContext<ProjectManagerDbContext>((serviceProvider, options) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            ProjectManagerDatabase.Configure(options, databaseOptions.ConnectionString);
        });

        services.AddHealthChecks()
            .AddDbContextCheck<ProjectManagerDbContext>(ProjectManagerDatabase.HealthCheckName);
    }
}
