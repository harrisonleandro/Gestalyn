using System.Net;
using System.Text.Json;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Api;

public sealed class HealthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Get_ReturnsHealthyStatus()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = await ReadJsonAsync(response);
        var root = document.RootElement;
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task Get_ReportsOnlyChecksForConfiguredComponents()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
        using var document = await ReadJsonAsync(response);

        var checkNames = document.RootElement
            .GetProperty("checks")
            .EnumerateArray()
            .Select(check => check.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();

        // Banco de dados e serviços externos ainda não existem e não podem ser reportados como saudáveis.
        Assert.Equal(["self"], checkNames);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
    }
}
