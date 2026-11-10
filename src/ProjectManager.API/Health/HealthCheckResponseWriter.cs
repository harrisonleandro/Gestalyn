using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ProjectManager.API.Health;

/// <summary>
/// Serializa o <see cref="HealthReport"/> em JSON, listando somente as verificações registradas.
/// </summary>
internal static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly string ApplicationVersion =
        typeof(HealthCheckResponseWriter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        var timeProvider = context.RequestServices.GetRequiredService<TimeProvider>();

        var response = new HealthResponse(
            Status: report.Status.ToString(),
            Version: ApplicationVersion,
            CheckedAt: timeProvider.GetUtcNow(),
            TotalDurationMs: report.TotalDuration.TotalMilliseconds,
            Checks: [.. report.Entries.Select(entry => new HealthCheckEntryResponse(
                Name: entry.Key,
                Status: entry.Value.Status.ToString(),
                Description: entry.Value.Description,
                DurationMs: entry.Value.Duration.TotalMilliseconds))]);

        context.Response.ContentType = "application/json; charset=utf-8";
        return JsonSerializer.SerializeAsync(context.Response.Body, response, SerializerOptions, context.RequestAborted);
    }

    private sealed record HealthResponse(
        string Status,
        string Version,
        DateTimeOffset CheckedAt,
        double TotalDurationMs,
        IReadOnlyList<HealthCheckEntryResponse> Checks);

    private sealed record HealthCheckEntryResponse(
        string Name,
        string Status,
        string? Description,
        double DurationMs);
}
