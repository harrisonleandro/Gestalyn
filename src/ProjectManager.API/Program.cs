using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ProjectManager.API.Activity;
using ProjectManager.API.Authentication;
using ProjectManager.API.ErrorHandling;
using ProjectManager.API.Health;
using ProjectManager.API.Organizations;
using ProjectManager.Application;
using ProjectManager.Application.Abstractions.Activity;
using ProjectManager.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure();

// Autor e origem das atividades a partir da requisição (ADR-014).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IActivityContext, HttpActivityContext>();

// Seguro por padrão: todo endpoint tem uma política explícita por esquema (ADR-016), salvo [AllowAnonymous].
builder.Services.AddControllers(options => options.Conventions.Add(new ApiPolicyConvention()))
    // Enumerações (prioridade, papéis de projeto, provedor) trafegam pelo nome, não pelo número.
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Gestalyn API";
    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApiHealthChecks();
builder.Services.AddApiAuthentication();
builder.Services.AddAuthRateLimiting();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRateLimiter();
app.UseAuthentication();
// Rotas de agentes: limite de falhas por IP, autenticação do agente, rate limiting da credencial e auditoria
// de cada requisição (ADR-016, D3, D10 e D11).
app.UseMiddleware<ProjectManager.API.Agents.AgentRequestMiddleware>();
// Organização ativa: depende do usuário autenticado e da rota; antes da autorização dos endpoints.
app.UseMiddleware<ActiveOrganizationMiddleware>();
app.UseAuthorization();

app.MapHealthChecks(HealthEndpoints.Path, new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
});
app.MapControllers();

// Monta os endpoints já na inicialização: uma rota de agentes sem [AgentApi], ou o contrário, impede a API de
// iniciar (ApiPolicyConvention), em vez de falhar só na primeira requisição.
foreach (var dataSource in ((IEndpointRouteBuilder)app).DataSources)
{
    _ = dataSource.Endpoints;
}

await app.RunAsync();
