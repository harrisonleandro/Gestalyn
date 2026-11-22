using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ProjectManager.Domain.Agents;

namespace ProjectManager.API.Agents;

/// <summary>
/// O que a requisição de agente vai deixar em <see cref="AgentOperation"/> (ADR-016, D10): a credencial identificada
/// (válida ou recusada) e o primeiro motivo de recusa. Preenchido pelo handler de autenticação, pela guarda de
/// organização, pela decisão de acesso ao projeto e pelas regras de negócio; gravado pelo middleware no fim.
/// </summary>
internal sealed class AgentOperationContext
{
    public Guid? OrganizationId { get; private set; }

    public Guid? AgentId { get; private set; }

    public Guid? CredentialId { get; private set; }

    public AgentAuthorizationResult Result { get; private set; } = AgentAuthorizationResult.Allowed;

    public string? DenialCode { get; private set; }

    /// <summary>Requisições bloqueadas por limite depois da primeira da janela não são gravadas.</summary>
    public bool SkipRecording { get; set; }

    public bool IsIdentified => CredentialId is not null;

    public void Identify(Guid organizationId, Guid agentId, Guid credentialId)
    {
        OrganizationId = organizationId;
        AgentId = agentId;
        CredentialId = credentialId;
    }

    /// <summary>O primeiro motivo vale: uma recusa posterior não o substitui.</summary>
    public void Deny(AgentAuthorizationResult result, string code)
    {
        if (Result == AgentAuthorizationResult.Allowed && result != AgentAuthorizationResult.Allowed)
        {
            Result = result;
            DenialCode = code;
        }
    }

    /// <summary>Código de uma regra de negócio que recusou a operação (por exemplo, <c>agent_cannot_approve</c>).</summary>
    public void DenyByRule(string code) => Deny(AgentAuthorizationResult.BusinessRuleDenied, code);
}

/// <summary>Limite de falhas por IP para tokens <c>gsk_</c> inexistentes ou malformados (seção <c>RateLimiting:AgentAuthFailures</c>, D11).</summary>
internal sealed class AgentAuthFailureOptions
{
    public const string SectionName = "RateLimiting:AgentAuthFailures";

    public int PermitLimit { get; set; } = 30;

    public int WindowSeconds { get; set; } = 60;

    /// <summary>Quantidade máxima de IPs acompanhados ao mesmo tempo: limita a memória sob um ataque distribuído.</summary>
    public int MaxTrackedAddresses { get; set; } = 100_000;

    internal bool IsValid() => PermitLimit > 0 && WindowSeconds is > 0 and <= 3600 && MaxTrackedAddresses > 0;
}

/// <summary>
/// Proteção barata da consulta de credenciais (D11): conta, em memória e por IP, os tokens <c>gsk_</c> inexistentes ou
/// malformados numa janela fixa. Acima do limite, o IP recebe 429 antes de qualquer consulta ao banco. Não é a cota
/// dos agentes. Atrás de um proxy, depende dos forwarded headers (Fase 14).
/// </summary>
internal sealed class AgentAuthFailureLimiter : IDisposable
{
    private readonly MemoryCache _windows;
    private readonly AgentAuthFailureOptions _options;
    private readonly TimeProvider _timeProvider;

    public AgentAuthFailureLimiter(IOptions<AgentAuthFailureOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _timeProvider = timeProvider;
        _windows = new MemoryCache(new MemoryCacheOptions { SizeLimit = _options.MaxTrackedAddresses });
    }

    /// <summary>Se o IP passou do limite, devolve os segundos até o fim da janela.</summary>
    public bool IsBlocked(IPAddress? address, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (!_windows.TryGetValue(Key(address), out FailureWindow? window) || window is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        if (window.EndsAt <= now || Volatile.Read(ref window.Count) < _options.PermitLimit)
        {
            return false;
        }

        retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((window.EndsAt - now).TotalSeconds));
        return true;
    }

    public void RecordFailure(IPAddress? address)
    {
        var now = _timeProvider.GetUtcNow();
        var key = Key(address);
        if (!_windows.TryGetValue(key, out FailureWindow? window) || window is null || window.EndsAt <= now)
        {
            window = new FailureWindow(now.AddSeconds(_options.WindowSeconds));
            _windows.Set(key, window, new MemoryCacheEntryOptions { AbsoluteExpiration = window.EndsAt, Size = 1 });
        }

        Interlocked.Increment(ref window.Count);
    }

    public void Dispose() => _windows.Dispose();

    private static string Key(IPAddress? address) => address?.ToString() ?? "desconhecido";

    private sealed class FailureWindow(DateTimeOffset endsAt)
    {
        public int Count;

        public DateTimeOffset EndsAt { get; } = endsAt;
    }
}
