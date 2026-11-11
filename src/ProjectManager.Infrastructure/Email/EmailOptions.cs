using MailKit.Security;

namespace ProjectManager.Infrastructure.Email;

/// <summary>
/// Servidor SMTP (seção <c>Email</c>). Em desenvolvimento e nos testes, aponta para o Mailpit.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Gestalyn";

    internal bool IsValid() =>
        !string.IsNullOrWhiteSpace(Host)
        && Port is > 0 and <= 65535
        && !string.IsNullOrWhiteSpace(FromAddress)
        && FromAddress.Contains('@', StringComparison.Ordinal);
}
