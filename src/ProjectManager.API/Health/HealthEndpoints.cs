using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ProjectManager.API.Health;

internal static class HealthEndpoints
{
    public const string Path = "/health";

    public const string SelfCheckName = "self";

    /// <summary>
    /// Registra apenas verificações de componentes que realmente existem.
    /// Banco de dados e serviços externos serão adicionados quando forem configurados.
    /// </summary>
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck(
                SelfCheckName,
                () => HealthCheckResult.Healthy("O processo da API está em execução."));

        return services;
    }
}
