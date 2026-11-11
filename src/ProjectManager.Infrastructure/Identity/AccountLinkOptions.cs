namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Endereço da interface Web usado nos links enviados por e-mail (confirmação e redefinição de senha).
/// </summary>
public sealed class AccountLinkOptions
{
    public const string SectionName = "Authentication:AccountLinks";

    public Uri? WebBaseUrl { get; set; }
}
