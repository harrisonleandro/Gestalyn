using Microsoft.Extensions.DependencyInjection;

namespace ProjectManager.Application;

/// <summary>
/// Ponto de composição da camada de aplicação.
/// Casos de uso, validadores e serviços de aplicação serão registrados aqui a partir da Fase 02.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
