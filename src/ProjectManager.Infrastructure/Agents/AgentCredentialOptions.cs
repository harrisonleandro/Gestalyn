using ProjectManager.Domain.Agents;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>Validade das credenciais de agentes (seção <c>Agents:Credentials</c>, ADR-016).</summary>
public sealed class AgentCredentialOptions
{
    public const string SectionName = "Agents:Credentials";

    public int DefaultLifetimeDays { get; set; } = 90;

    public int MaxLifetimeDays { get; set; } = AgentCredential.MaxLifetimeDays;

    internal bool IsValid() =>
        MaxLifetimeDays is >= AgentCredential.MinLifetimeDays and <= AgentCredential.MaxLifetimeDays
        && DefaultLifetimeDays >= AgentCredential.MinLifetimeDays
        && DefaultLifetimeDays <= MaxLifetimeDays;
}
