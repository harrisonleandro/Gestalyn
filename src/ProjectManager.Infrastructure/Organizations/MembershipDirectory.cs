using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Organizations;
using ProjectManager.Domain.Organizations;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Organizations;

/// <summary>
/// Exceção documentada ao filtro de organização (ADR-008): estas são as únicas consultas que ignoram a
/// organização ativa, e cada uma é restrita a uma prova de vínculo — o <c>userId</c> do token (associações
/// do próprio usuário) ou o hash de um token de convite.
/// </summary>
internal sealed class MembershipDirectory(ProjectManagerDbContext dbContext) : IMembershipDirectory
{
    public async Task<ActiveMembership?> FindMembershipAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var member = await dbContext.OrganizationMembers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId, cancellationToken);

        if (member is null)
        {
            return null;
        }

        var role = await dbContext.Roles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(r => r.Permissions)
            .SingleAsync(r => r.Id == member.RoleId && r.OrganizationId == organizationId, cancellationToken);

        return new ActiveMembership(
            organizationId,
            member.Id,
            userId,
            role.Id,
            role.IsOwner,
            role.Permissions.Select(permission => permission.PermissionCode).ToHashSet(StringComparer.Ordinal));
    }

    public async Task<IReadOnlyList<OrganizationSummary>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken) =>
        await (
            from member in dbContext.OrganizationMembers.IgnoreQueryFilters()
            where member.UserId == userId
            join organization in dbContext.Organizations.IgnoreQueryFilters() on member.OrganizationId equals organization.Id
            join role in dbContext.Roles.IgnoreQueryFilters()
                on new { member.OrganizationId, Id = member.RoleId } equals new { role.OrganizationId, role.Id }
            orderby organization.Name
            select new OrganizationSummary(organization.Id, organization.Name, role.Id, role.Name))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    /// <summary>Convite pelo hash do token (rastreado, para ser aceito na mesma unidade de trabalho).</summary>
    public Task<Invitation?> FindInvitationByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        dbContext.Invitations
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(invitation => invitation.TokenHash == tokenHash, cancellationToken);
}
