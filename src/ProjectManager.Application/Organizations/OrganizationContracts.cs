using System.ComponentModel.DataAnnotations;

namespace ProjectManager.Application.Organizations;

public sealed class OrganizationNameRequest
{
    [Required(ErrorMessage = "Informe o nome da organização.")]
    [StringLength(200, ErrorMessage = "O nome deve ter no máximo {1} caracteres.")]
    public required string Name { get; init; }
}

public sealed class ChangeMemberRoleRequest
{
    [Required]
    public required Guid RoleId { get; init; }
}

public sealed class CreateInvitationRequest
{
    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [StringLength(256)]
    public required string Email { get; init; }

    [Required]
    public required Guid RoleId { get; init; }
}

public sealed class AcceptInvitationRequest
{
    [Required]
    [StringLength(2048)]
    public required string Token { get; init; }
}

/// <summary>Organização da qual o usuário participa, com o papel dele.</summary>
public sealed record OrganizationSummary(Guid Id, string Name, Guid RoleId, string RoleName);

public sealed record OrganizationDetails(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record MemberDetails(Guid UserId, string DisplayName, string Email, Guid RoleId, string RoleName, DateTimeOffset JoinedAt);

public sealed record RoleDetails(Guid Id, string Name, string? SystemRole, IReadOnlyList<string> Permissions);

public sealed record InvitationDetails(Guid Id, string Email, Guid RoleId, string RoleName, string Status, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt);

public sealed record AcceptedInvitation(Guid OrganizationId, string OrganizationName, string RoleName);

public static class InvitationStatus
{
    public const string Pending = "Pendente";
    public const string Accepted = "Aceito";
    public const string Revoked = "Revogado";
    public const string Expired = "Expirado";
}
