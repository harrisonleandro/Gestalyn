using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.IntegrationTests.Infrastructure;

/// <summary>
/// Hospeda a ProjectManager.API em memória, conectada ao banco e, opcionalmente, ao Mailpit.
/// Usa uma chave de assinatura JWT aleatória por instância e, se informado, um relógio controlado.
/// </summary>
public sealed class ApiFactory(
    string connectionString,
    MailpitFixture? mailpit = null,
    IReadOnlyDictionary<string, string?>? overrides = null,
    TimeProvider? timeProvider = null) : WebApplicationFactory<Program>
{
    public const string WebBaseUrl = "http://web.test/";

    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public TestLogCollector Logs { get; } = new();

    /// <summary>Diretório temporário e exclusivo desta instância para os anexos.</summary>
    public string AttachmentsRoot { get; } = Path.Combine(Path.GetTempPath(), "gestalyn-tests", Guid.NewGuid().ToString("N"));

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
            // Idem para os agentes; a limpeza em segundo plano fica desligada e os testes executam o ciclo diretamente.
            ["RateLimiting:Agents:PermitLimit"] = "100000",
            ["RateLimiting:AgentAuthFailures:PermitLimit"] = "100000",
            ["Agents:Retention:Enabled"] = "false",
            ["Storage:Attachments:RootPath"] = AttachmentsRoot,
        };

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        // Adicionado por último: prevalece sobre appsettings, user-secrets e variáveis de ambiente.
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        // Relógio controlado pelo teste (ex.: janela de repetição do refresh token).
        if (timeProvider is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(timeProvider));
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(AttachmentsRoot))
        {
            Directory.Delete(AttachmentsRoot, recursive: true);
        }
    }
}
