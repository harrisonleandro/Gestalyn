namespace ProjectManager.Infrastructure.Identity;

/// <summary>O que fazer com um refresh token apresentado (ADR-012).</summary>
internal enum RefreshAction
{
    /// <summary>Token inválido para a sessão (expirada ou já revogada): 401, sem alterar nada.</summary>
    Reject,

    /// <summary>Token ainda não usado: consome e emite o sucessor.</summary>
    Rotate,

    /// <summary>Requisição simultânea dentro da janela: devolve o mesmo sucessor, sem nova rotação.</summary>
    Replay,

    /// <summary>Reuso: revoga a sessão inteira e responde 401.</summary>
    RevokeForReuse,
}

/// <summary>
/// Regra única de decisão do refresh. Não existe "401 sem revogar" para um token já trocado: ou a
/// requisição é uma repetição legítima (mesma cadeia), ou a sessão é revogada.
/// </summary>
internal static class RefreshDecision
{
    /// <param name="successorReplayable">
    /// O sucessor registrado existe, ainda não foi usado e corresponde ao sucessor derivado do token
    /// apresentado.
    /// </param>
    public static RefreshAction Decide(
        UserRefreshToken token,
        bool successorReplayable,
        DateTimeOffset now,
        TimeSpan replayWindow,
        int maxReplays)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (!token.Session.IsActive(now) || token.ExpiresAt <= now)
        {
            return RefreshAction.Reject;
        }

        if (token.ConsumedAt is not { } consumedAt)
        {
            return RefreshAction.Rotate;
        }

        var withinWindow = now - consumedAt <= replayWindow;
        var replayable = withinWindow
            && token.ReplacedByTokenId is not null
            && successorReplayable
            && token.ReplayCount < maxReplays;

        return replayable ? RefreshAction.Replay : RefreshAction.RevokeForReuse;
    }
}
