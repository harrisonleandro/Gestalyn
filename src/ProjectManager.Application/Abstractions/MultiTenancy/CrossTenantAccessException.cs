namespace ProjectManager.Application.Abstractions.MultiTenancy;

/// <summary>
/// Tentativa de gravar dados fora da organização ativa, ou sem organização ativa.
/// </summary>
public sealed class CrossTenantAccessException : InvalidOperationException
{
    public CrossTenantAccessException()
        : base("Operação bloqueada: os dados não pertencem à organização ativa.")
    {
    }

    public CrossTenantAccessException(string message)
        : base(message)
    {
    }

    public CrossTenantAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
