using Microsoft.AspNetCore.Identity;
using ProjectManager.Domain.Common;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Usuário da plataforma (global, não pertence a uma organização). O e-mail é também o nome de usuário.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>, IAuditable
{
    public ApplicationUser() => Id = Guid.CreateVersion7();

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
