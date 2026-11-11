using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Api;

public sealed class ErrorHandlingTests(PostgresDatabaseFixture database) : IDisposable
{
    private readonly ApiFactory _factory = new(database.ConnectionString);

    private const string ProblemJsonMediaType = "application/problem+json";

    [Fact]
    public async Task UnknownRoute_ReturnsNotFoundProblemDetails()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/rota-inexistente", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJsonMediaType, response.Content.Headers.ContentType?.MediaType);

        using var problem = await ReadJsonAsync(response);
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task UnhandledException_ReturnsInternalServerErrorProblemDetailsWithoutInternalDetails()
    {
        using var client = CreateClientWithThrowingEndpoint();

        using var response = await client.GetAsync(new Uri(ThrowingTestController.Route, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJsonMediaType, response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(ThrowingTestController.SensitiveMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);

        using var problem = JsonDocument.Parse(body);
        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _), "A resposta deve conter traceId para correlação com os logs.");
    }

    private HttpClient CreateClientWithThrowingEndpoint() =>
        _factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(ThrowingTestController).Assembly)))
            .CreateClient();

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
    }

    public void Dispose() => _factory.Dispose();
}
