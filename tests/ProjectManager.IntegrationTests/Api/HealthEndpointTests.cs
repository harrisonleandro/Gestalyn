using System.Net;
using System.Text.Json;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Api;

public sealed class HealthEndpointTests(PostgresDatabaseFixture database)
{
    private static readonly Uri HealthUri = new("/health", UriKind.Relative);

    // Porta sem servidor: a conexão é recusada imediatamente.
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=indisponivel;Username=indisponivel;Password=senha-de-teste;Timeout=3";

    [Fact]
    public async Task Get_WithDatabaseAvailable_ReturnsHealthy()
    {
        using var factory = new ApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(HealthUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = await ReadJsonAsync(response);
        var root = document.RootElement;
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));
        Assert.Equal("Healthy", CheckStatus(root, "database"));
    }

    [Fact]
    public async Task Get_ReportsOnlyChecksForConfiguredComponents()
    {
        using var factory = new ApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(HealthUri, TestContext.Current.CancellationToken);
        using var document = await ReadJsonAsync(response);

        var checkNames = document.RootElement
            .GetProperty("checks")
            .EnumerateArray()
            .Select(check => check.GetProperty("name").GetString() ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["database", "self"], checkNames);
    }

    [Fact]
    public async Task Get_WithDatabaseUnavailable_ReturnsServiceUnavailableWithoutLeakingDetails()
    {
        using var factory = new ApiFactory(UnreachableConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(HealthUri, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var document = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", CheckStatus(document.RootElement, "database"));
        Assert.Equal("Healthy", CheckStatus(document.RootElement, "self"));
        Assert.DoesNotContain("senha-de-teste", body, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
    }

    private static string? CheckStatus(JsonElement root, string checkName) =>
        root.GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == checkName)
            .GetProperty("status")
            .GetString();

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
    }
}
