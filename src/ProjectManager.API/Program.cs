using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ProjectManager.API.ErrorHandling;
using ProjectManager.API.Health;
using ProjectManager.Application;
using ProjectManager.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure();

builder.Services.AddControllers();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Gestalyn API";
    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApiHealthChecks();

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

app.MapHealthChecks(HealthEndpoints.Path, new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteAsync,
});
app.MapControllers();

await app.RunAsync();
