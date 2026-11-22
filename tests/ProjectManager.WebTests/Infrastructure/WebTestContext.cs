using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProjectManager.Web;
using ProjectManager.Web.Api;
using ProjectManager.Web.Markdown;

namespace ProjectManager.WebTests.Infrastructure;

/// <summary>
/// Contexto bUnit com a Web montada de verdade (clientes, Display, Markdown), trocando só a rede: a API é um
/// <see cref="StubApiHandler"/> com respostas definidas por cada teste.
/// </summary>
public sealed class WebTestContext : BunitContext
{
    public static readonly Guid OrganizationId = Guid.Parse("01a1df32-0000-7000-8000-000000000001");
    public static readonly Guid ProjectId = Guid.Parse("01a1df32-0000-7000-8000-000000000002");
    public static readonly Guid UserId = Guid.Parse("01a1df32-0000-7000-8000-000000000003");

    public WebTestContext()
    {
        Services.AddSingleton(Api);
        Services.AddSingleton<IApiRequestContext>(new FixedRequestContext());
        Services.AddSingleton(sp => new GestalynApi(
            new HttpClient(Api) { BaseAddress = new Uri("http://api.test/") },
            sp.GetRequiredService<IApiRequestContext>(),
            Logs));
        Services.AddScoped<OrganizationsApi>();
        Services.AddScoped<MembershipCache>();
        Services.AddScoped<ProjectsApi>();
        Services.AddScoped<PlanningApi>();
        Services.AddScoped<TasksApi>();
        Services.AddScoped<WorkflowApi>();
        Services.AddScoped<DocumentsApi>();
        Services.AddScoped<AgentsApi>();
        Services.AddScoped<WorkReportsApi>();
        Services.AddSingleton<MarkdownRenderer>();
        Services.AddSingleton<Microsoft.AspNetCore.Http.IHttpContextAccessor>(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        Services.AddSingleton(new Display(Options.Create(new DisplayOptions { TimeZone = "UTC" })));

        var authorization = AddAuthorization();
        authorization.SetAuthorized("Pessoa de Teste");
        authorization.SetClaims(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, UserId.ToString()));
    }

    public StubApiHandler Api { get; } = new();

    /// <summary>Tudo o que o cliente da API registrou em log durante o teste.</summary>
    public CapturingLogger<GestalynApi> Logs { get; } = new();

    /// <summary>Organização em que o usuário tem as permissões informadas.</summary>
    public void Membership(params string[] permissions) =>
        Api.Json(HttpMethod.Get, $"organizations/{OrganizationId}/me", new MyMembership(OrganizationId, "Riuk Labs", Guid.NewGuid(), "Membro", false, permissions));

    /// <summary>Projeto em que o usuário tem as permissões informadas.</summary>
    public ProjectDetails Project(params string[] permissions)
    {
        var project = new ProjectDetails(
            ProjectId, "Portal", null, Priority.Medium, null, null, [], null, null, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, ProjectRole.Contributor, ["project.read", .. permissions]);
        Api.Json(HttpMethod.Get, $"projects/{ProjectId}", project);
        return project;
    }

    private sealed class FixedRequestContext : IApiRequestContext
    {
        public CancellationToken RequestAborted => CancellationToken.None;

        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("token-de-teste");
    }
}

/// <summary>API falsa: responde por método e caminho (sem a query string) e registra as chamadas.</summary>
public sealed class StubApiHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, (HttpStatusCode Status, string Body, string ContentType)> _responses = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    /// <summary>Corpo da última requisição de cada método e caminho.</summary>
    public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);

    public void Json(HttpMethod method, string path, object body, HttpStatusCode status = HttpStatusCode.OK) =>
        _responses[Key(method, path)] = (status, JsonSerializer.Serialize(body, JsonOptions), "application/json");

    public void Problem(HttpMethod method, string path, HttpStatusCode status, string detail) =>
        _responses[Key(method, path)] = (status, JsonSerializer.Serialize(new { title = "Erro", detail, status = (int)status }), "application/problem+json");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        path = path.StartsWith("api/v1/", StringComparison.Ordinal) ? path["api/v1/".Length..] : path;
        var key = Key(request.Method, path);
        Calls.Add(key + request.RequestUri.Query);
        if (request.Content is not null)
        {
            Bodies[key] = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        }

        var (status, body, contentType) = _responses.TryGetValue(key, out var response)
            ? response
            : (HttpStatusCode.NotFound, "{\"title\":\"Não encontrado\",\"status\":404}", "application/problem+json");
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) });
    }

    public static string Key(HttpMethod method, string path) => $"{method.Method} {path}";
}

/// <summary>Logger que guarda as mensagens formatadas, para conferir que nenhum segredo vai para o log.</summary>
public sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Messages.Add(formatter(state, exception) + exception);
    }
}
