using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ProjectManager.Infrastructure;

/// <summary>
/// Ponto de composição da camada de infraestrutura.
/// Persistência (EF Core + PostgreSQL) e integrações externas serão registradas aqui em fases futuras.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Relógio do sistema injetável, substituível em testes por um TimeProvider controlado.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
