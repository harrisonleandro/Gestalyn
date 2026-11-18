using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ProjectManager.API.Authentication;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Api;

/// <summary>
/// Políticas por esquema (ADR-016): todo endpoint da API tem exatamente uma política explícita, ou é anônimo por
/// uma razão conhecida. Uma rota nova sem política, com duas políticas ou anônima por engano faz o teste falhar.
/// </summary>
public sealed class EndpointPolicyTests(PostgresDatabaseFixture database) : IDisposable
{
    /// <summary>Política das rotas de agentes, criada com o esquema <c>AgentToken</c> (ADR-016, etapa 4 do plano).</summary>
    private const string AgentPolicy = "Agent";

    private const string AgentRoutePrefix = "api/v1/agents/";

    /// <summary>Únicas rotas anônimas: entrada, cadastro e recuperação de conta (ADR-006).</summary>
    private static readonly HashSet<string> AnonymousRoutes = new(StringComparer.Ordinal)
    {
        "POST api/v1/auth/register",
        "POST api/v1/auth/confirm-email",
        "POST api/v1/auth/resend-confirmation",
        "POST api/v1/auth/login",
        "POST api/v1/auth/refresh",
        "POST api/v1/auth/forgot-password",
        "POST api/v1/auth/reset-password",
    };

    private readonly ApiFactory _factory = new(database.ConnectionString);

    [Fact]
    public void EveryApiEndpoint_HasExactlyOneSchemePolicy_OrIsAKnownAnonymousRoute()
    {
        var endpoints = ApiEndpoints();
        Assert.NotEmpty(endpoints);

        var anonymous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints)
        {
            var route = endpoint.RoutePattern.RawText!.TrimStart('/');
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var name = $"{string.Join(',', methods)} {route}";

            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                anonymous.Add(name);
                continue;
            }

            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).ToList();
            var expected = route.StartsWith(AgentRoutePrefix, StringComparison.Ordinal) ? AgentPolicy : ApiPolicies.User;

            Assert.True(
                policies.Count == 1 && policies[0] == expected,
                $"{name}: esperada só a política '{expected}', encontrada [{string.Join(", ", policies)}].");
        }

        Assert.Equal(AnonymousRoutes.Order(StringComparer.Ordinal), anonymous.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OpaqueAgentToken_IsRejectedOnUserRoutes()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/auth/me", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "gsk_" + new string('A', 43));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();

    private List<RouteEndpoint> ApiEndpoints() =>
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("api/", StringComparison.Ordinal) == true)
            .ToList();
}
