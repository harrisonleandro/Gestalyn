using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Api;

public sealed class OpenApiDocumentTests(PostgresDatabaseFixture database) : IDisposable
{
    private readonly ApiFactory _factory = new(database.ConnectionString);

    private static readonly Uri OpenApiDocumentUri = new("/openapi/v1.json", UriKind.Relative);

    [Fact]
    public async Task Development_ExposesOpenApiDocument()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(OpenApiDocumentUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_DoesNotExposeOpenApiDocument()
    {
        using var productionFactory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = productionFactory.CreateClient();

        using var response = await client.GetAsync(OpenApiDocumentUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
