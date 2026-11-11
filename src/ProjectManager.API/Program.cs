using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc.Authorization;
using ProjectManager.API.Authentication;
using ProjectManager.API.ErrorHandling;
using ProjectManager.API.Health;
using ProjectManager.API.Organizations;
using ProjectManager.Application;
using ProjectManager.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure();

// Seguro por padrão: todo controller exige autenticação, salvo [AllowAnonymous] explícito.
builder.Services.AddControllers(options => options.Filters.Add(new AuthorizeFilter()));
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
// Organização ativa: depende do usuário autenticado e da rota; antes da autorização dos endpoints.
app.UseMiddleware<ActiveOrganizationMiddleware>();
app.UseAuthorization();

app.MapHealthChecks(HealthEndpoints.Path, new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
});
app.MapControllers();

await app.RunAsync();
