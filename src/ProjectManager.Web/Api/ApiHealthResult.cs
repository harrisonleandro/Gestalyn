namespace ProjectManager.Web.Api;

/// <summary>
/// Resultado da consulta ao endpoint de saúde da API, do ponto de vista da Web.
/// </summary>
public sealed record ApiHealthResult(bool IsReachable, string? Status, string? Version, string? Error)
{
    public static ApiHealthResult Reachable(string status, string? version) => new(true, status, version, null);

    public static ApiHealthResult Unreachable(string error) => new(false, null, null, error);
}
