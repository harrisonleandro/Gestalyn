using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.IntegrationTests.Infrastructure;

/// <summary>
/// Hospeda a ProjectManager.API em memória, conectada ao banco e, opcionalmente, ao Mailpit.
/// Usa uma chave de assinatura JWT aleatória por instância.
/// </summary>
public sealed class ApiFactory(
    string connectionString,
    MailpitFixture? mailpit = null,
    IReadOnlyDictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    public const string WebBaseUrl = "http://web.test/";

    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public TestLogCollector Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        var settings = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{ProjectManagerDatabase.ConnectionStringName}"] = connectionString,
            ["Authentication:Jwt:SigningKey"] = SigningKey,
            ["Authentication:AccountLinks:WebBaseUrl"] = WebBaseUrl,
            // Sem Mailpit, aponta para uma porta sem servidor: os testes que não usam e-mail não dependem dele.
            ["Email:Host"] = mailpit?.SmtpHost ?? "127.0.0.1",
            ["Email:Port"] = (mailpit?.SmtpPort ?? 1).ToString(CultureInfo.InvariantCulture),
            ["Email:Security"] = "None",
            ["Email:FromAddress"] = "nao-responda@gestalyn.test",
            // Limite alto: apenas o teste de rate limiting usa um limite baixo.
            ["RateLimiting:Auth:PermitLimit"] = "100000",
        };

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        // Adicionado por último: prevalece sobre appsettings, user-secrets e variáveis de ambiente.
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }
}
