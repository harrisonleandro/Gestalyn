using ProjectManager.Domain.Workflows;

namespace ProjectManager.Domain.Activity;

/// <summary>Por onde chegou a operação (ADR-014). É declarada pelo cliente: serve para entender o uso, não para autorizar.</summary>
public enum ActivityOrigin
{
    Api,
    Web,
    Cli,
    Mcp,
    System,
}

/// <summary>
/// Quem fez a operação e por onde. O autor vem sempre da credencial (o <c>sub</c> do token); a origem, do
/// cabeçalho <c>X-Gestalyn-Client</c>; o ID da requisição correlaciona o registro com os logs.
/// </summary>
public sealed record ActivityActor(Guid? ActorId, ActorKind Kind, ActivityOrigin Origin, string? RequestId)
{
    public const int RequestIdMaxLength = 100;

    public static ActivityActor System { get; } = new(null, ActorKind.User, ActivityOrigin.System, null);

    /// <summary>ID da requisição limitado ao tamanho da coluna.</summary>
    public string? BoundedRequestId => RequestId is { Length: > RequestIdMaxLength } id ? id[..RequestIdMaxLength] : RequestId;
}

public static class ActivityOrigins
{
    /// <summary>
    /// Origem declarada pelo cliente. Sem o cabeçalho, ou com um valor desconhecido, a origem é a API; <c>System</c>
    /// nunca vem de fora.
    /// </summary>
    public static ActivityOrigin FromClientHeader(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "WEB" => ActivityOrigin.Web,
        "CLI" => ActivityOrigin.Cli,
        "MCP" => ActivityOrigin.Mcp,
        _ => ActivityOrigin.Api,
    };
}
