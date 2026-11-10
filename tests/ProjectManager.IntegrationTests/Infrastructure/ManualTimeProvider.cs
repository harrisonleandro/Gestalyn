namespace ProjectManager.IntegrationTests.Infrastructure;

/// <summary>
/// Relógio controlado pelo teste.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
