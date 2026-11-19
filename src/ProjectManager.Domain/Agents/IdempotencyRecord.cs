using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Agents;

/// <summary>
/// Resposta guardada de um <c>POST</c> de agente (ADR-016, D9). A chave vale por credencial e fica presa ao método, ao
/// modelo da rota e ao SHA-256 do corpo. Gravada na mesma transação da operação, expira logicamente em 24 horas e é
/// apagada depois de 7 dias pela limpeza de retenção.
/// </summary>
public sealed class IdempotencyRecord : Entity, IOrganizationOwned
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotency-Replayed";
    public const int KeyMaxLength = 100;
    public const int MethodMaxLength = 10;
    public const int RouteMaxLength = 200;
    public const int LocationMaxLength = 500;
    public const int ResponseBodyMaxLength = 256 * 1024;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private static readonly SearchValues<char> VisibleAscii = SearchValues.Create(
        Enumerable.Range(0x21, 0x7E - 0x21 + 1).Select(code => (char)code).ToArray());

    public IdempotencyRecord(
        Guid organizationId,
        Guid agentId,
        Guid credentialId,
        string key,
        string method,
        string routeTemplate,
        string requestHash,
        int responseStatus,
        string responseBody,
        string? location,
        Guid? projectId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(responseBody);

        OrganizationId = Guard.RequiredId(organizationId, nameof(organizationId));
        AgentId = Guard.RequiredId(agentId, nameof(agentId));
        CredentialId = Guard.RequiredId(credentialId, nameof(credentialId));
        Key = IsValidKey(key) ? key : throw new ArgumentException("Chave de idempotência inválida.", nameof(key));
        Method = Guard.RequiredText(method, MethodMaxLength, nameof(method));
        RouteTemplate = Guard.RequiredText(routeTemplate, RouteMaxLength, nameof(routeTemplate));
        RequestHash = AgentTokens.IsHash(requestHash) ? requestHash : throw new ArgumentException("Hash do corpo inválido.", nameof(requestHash));
        ResponseStatus = responseStatus;
        ResponseBody = responseBody.Length <= ResponseBodyMaxLength
            ? responseBody
            : throw new ArgumentException("Resposta grande demais para guardar.", nameof(responseBody));
        Location = Guard.OptionalText(location, LocationMaxLength, nameof(location));
        ProjectId = projectId;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private IdempotencyRecord(string key, string method, string routeTemplate, string requestHash, string responseBody)
    {
        Key = key;
        Method = method;
        RouteTemplate = routeTemplate;
        RequestHash = requestHash;
        ResponseBody = responseBody;
    }

    public Guid OrganizationId { get; private set; }

    public Guid AgentId { get; private set; }

    public Guid CredentialId { get; private set; }

    public string Key { get; private set; }

    public string Method { get; private set; }

    public string RouteTemplate { get; private set; }

    /// <summary>SHA-256 do caminho concreto, da query e do corpo, em hexadecimal maiúsculo.</summary>
    public string RequestHash { get; private set; }

    public int ResponseStatus { get; private set; }

    public string ResponseBody { get; private set; }

    public string? Location { get; private set; }

    /// <summary>Projeto da operação, para reavaliar o acesso antes de devolver a resposta guardada.</summary>
    public Guid? ProjectId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>De 1 a 100 caracteres ASCII visíveis (0x21 a 0x7E).</summary>
    public static bool IsValidKey(string? key) =>
        key is { Length: > 0 and <= KeyMaxLength } && !key.AsSpan().ContainsAnyExcept(VisibleAscii);

    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAt;
}
