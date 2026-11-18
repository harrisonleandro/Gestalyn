namespace ProjectManager.Application.Abstractions.Authentication;

/// <summary>
/// Claims do access token emitido pela API.
/// </summary>
public static class AuthClaimTypes
{
    public const string Subject = "sub";

    public const string SessionId = "sid";

    public const string TokenId = "jti";

    /// <summary>Tipo do ator (ADR-016): presente só nos principais de agentes, com o valor <see cref="AgentActor"/>.</summary>
    public const string ActorKind = "actor_kind";

    public const string AgentActor = "agent";

    /// <summary>Organização do agente, vinda da credencial.</summary>
    public const string Organization = "org";

    /// <summary>Credencial de agente usada na requisição.</summary>
    public const string CredentialId = "cid";

    /// <summary>Escopo da credencial de agente (uma claim por escopo).</summary>
    public const string Scope = "scope";
}
