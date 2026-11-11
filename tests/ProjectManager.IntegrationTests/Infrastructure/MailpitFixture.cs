using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

[assembly: AssemblyFixture(typeof(ProjectManager.IntegrationTests.Infrastructure.MailpitFixture))]

namespace ProjectManager.IntegrationTests.Infrastructure;

/// <summary>
/// Servidor SMTP de teste (Mailpit) compartilhado pela suíte. Os e-mails enviados pela API são lidos
/// pela API HTTP do Mailpit, filtrando pelo destinatário (cada teste usa e-mails únicos).
/// </summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    public const string Image = "axllent/mailpit:v1.29";

    private const ushort SmtpContainerPort = 1025;
    private const ushort HttpContainerPort = 8025;
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(SmtpContainerPort, assignRandomHostPort: true)
        .WithPortBinding(HttpContainerPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
            request.ForPort(HttpContainerPort).ForPath("/api/v1/info")))
        .Build();

    private HttpClient? _httpClient;

    public string SmtpHost => _container.Hostname;

    public int SmtpPort => _container.GetMappedPublicPort(SmtpContainerPort);

    private HttpClient Http => _httpClient ?? throw new InvalidOperationException("Mailpit não iniciado.");

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpContainerPort)}/"),
        };
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>Aguarda um e-mail para o destinatário cujo assunto contenha o texto informado.</summary>
    public async Task<ReceivedEmail> WaitForEmailAsync(string to, string subjectContains, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + MessageTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var summary = (await SearchAsync(to, cancellationToken))
                .FirstOrDefault(message => message.Subject.Contains(subjectContains, StringComparison.Ordinal));

            if (summary is not null)
            {
                return await GetEmailAsync(summary, cancellationToken);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"Nenhum e-mail com o assunto '{subjectContains}' chegou em {MessageTimeout.TotalSeconds} s.");
    }

    public async Task<int> CountEmailsAsync(string to, CancellationToken cancellationToken) =>
        (await SearchAsync(to, cancellationToken)).Count;

    private async Task<List<EmailSummary>> SearchAsync(string to, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"to:\"{to}\"");
        using var document = await GetJsonAsync($"api/v1/search?query={query}", cancellationToken);

        return document.RootElement.GetProperty("messages")
            .EnumerateArray()
            .Select(message => new EmailSummary(
                message.GetProperty("ID").GetString()!,
                message.GetProperty("Subject").GetString() ?? string.Empty))
            .ToList();
    }

    private async Task<ReceivedEmail> GetEmailAsync(EmailSummary summary, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"api/v1/message/{summary.Id}", cancellationToken);

        return new ReceivedEmail(summary.Subject, document.RootElement.GetProperty("Text").GetString() ?? string.Empty);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private sealed record EmailSummary(string Id, string Subject);
}

public sealed record ReceivedEmail(string Subject, string TextBody)
{
    /// <summary>Extrai um parâmetro de consulta do primeiro link do e-mail.</summary>
    public string LinkParameter(string name)
    {
        var link = TextBody
            .Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .First(word => word.StartsWith("http", StringComparison.Ordinal) && word.Contains('?', StringComparison.Ordinal));

        var query = new Uri(link).Query.TrimStart('?').Split('&');
        var value = query.Single(pair => pair.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..];

        return Uri.UnescapeDataString(value);
    }
}
