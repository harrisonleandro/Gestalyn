using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.IntegrationTests.Infrastructure;

/// <summary>
/// Hospeda a ProjectManager.API em memória, conectada ao banco informado.
/// </summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Adicionado por último: prevalece sobre appsettings, user-secrets e variáveis de ambiente.
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{ProjectManagerDatabase.ConnectionStringName}"] = connectionString,
            }));
    }
}
