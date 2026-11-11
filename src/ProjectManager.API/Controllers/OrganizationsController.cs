using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Authentication;
using ProjectManager.API.Common;
using ProjectManager.API.Organizations;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Organizations;
using ProjectManager.Domain.Organizations;

namespace ProjectManager.API.Controllers;

/// <summary>
/// Organizações, membros, papéis e convites. As rotas com <c>{organizationId}</c> atuam sobre a
/// organização ativa, resolvida pela associação do usuário autenticado.
/// </summary>
[ApiController]
[Route("api/v1/organizations")]
public sealed class OrganizationsController(
    IOrganizationService organizations,
    IMembershipDirectory membershipDirectory,
    ITenantContext tenantContext) : ControllerBase
{
    private ActiveMembership Actor => tenantContext.Membership
        ?? throw new InvalidOperationException("Associação não resolvida.");

    /// <summary>Cria uma organização; o usuário autenticado vira Proprietário.</summary>
    [HttpPost]
    [ProducesResponseType<OrganizationSummary>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync(OrganizationNameRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetSession(out var userId, out _))
        {
            return Unauthorized();
        }

        var result = await organizations.CreateAsync(userId, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/organizations/{result.Value!.Id}", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>Organizações das quais o usuário autenticado participa, com o papel dele em cada uma.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OrganizationSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMineAsync(CancellationToken cancellationToken)
    {
        if (!User.TryGetSession(out var userId, out _))
        {
            return Unauthorized();
        }

        return Ok(await membershipDirectory.ListOrganizationsAsync(userId, cancellationToken));
    }

    [HttpGet("{organizationId:guid}")]
    [OrganizationPermission(OrganizationPermissions.OrganizationRead)]
    [ProducesResponseType<OrganizationDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await organizations.GetActiveAsync(cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPatch("{organizationId:guid}")]
    [OrganizationPermission(OrganizationPermissions.OrganizationUpdate)]
    [ProducesResponseType<OrganizationDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RenameAsync(OrganizationNameRequest request, CancellationToken cancellationToken)
    {
        var result = await organizations.RenameActiveAsync(request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{organizationId:guid}/members")]
    [OrganizationPermission(OrganizationPermissions.MembersRead)]
    [ProducesResponseType<IReadOnlyList<MemberDetails>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMembersAsync(CancellationToken cancellationToken) =>
        Ok(await organizations.ListMembersAsync(cancellationToken));

    [HttpPatch("{organizationId:guid}/members/{userId:guid}")]
    [OrganizationPermission(OrganizationPermissions.MembersUpdateRole)]
    [ProducesResponseType<MemberDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeMemberRoleAsync(Guid userId,
        ChangeMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await organizations.ChangeMemberRoleAsync(Actor, userId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Remove um membro. Qualquer membro pode remover a si mesmo (sair da organização).</summary>
    [HttpDelete("{organizationId:guid}/members/{userId:guid}")]
    [OrganizationPermission]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await organizations.RemoveMemberAsync(Actor, userId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }

    [HttpGet("{organizationId:guid}/roles")]
    [OrganizationPermission(OrganizationPermissions.MembersRead)]
    [ProducesResponseType<IReadOnlyList<RoleDetails>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRolesAsync(CancellationToken cancellationToken) =>
        Ok(await organizations.ListRolesAsync(cancellationToken));

    /// <summary>Convida um e-mail para a organização; o link de aceitação é enviado por e-mail.</summary>
    [HttpPost("{organizationId:guid}/invitations")]
    [OrganizationPermission(OrganizationPermissions.MembersInvite)]
    [ProducesResponseType<InvitationDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateInvitationAsync(
        Guid organizationId,
        CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await organizations.CreateInvitationAsync(Actor, request, cancellationToken);

        return result.Succeeded
            ? Created(new Uri($"/api/v1/organizations/{organizationId}/invitations", UriKind.Relative), result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpGet("{organizationId:guid}/invitations")]
    [OrganizationPermission(OrganizationPermissions.MembersInvite)]
    [ProducesResponseType<IReadOnlyList<InvitationDetails>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInvitationsAsync(CancellationToken cancellationToken) =>
        Ok(await organizations.ListInvitationsAsync(cancellationToken));

    [HttpDelete("{organizationId:guid}/invitations/{invitationId:guid}")]
    [OrganizationPermission(OrganizationPermissions.MembersInvite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        var result = await organizations.RevokeInvitationAsync(invitationId, cancellationToken);

        return result.Succeeded ? NoContent() : this.ToProblem(result.Error!);
    }
}
