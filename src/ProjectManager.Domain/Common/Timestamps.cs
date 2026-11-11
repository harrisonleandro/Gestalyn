namespace ProjectManager.Domain.Common;

public static class Timestamps
{
    /// <summary>
    /// Trunca para microssegundos, a precisão do PostgreSQL: o valor em memória fica igual ao lido do banco.
    /// </summary>
    public static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
}
