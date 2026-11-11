using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManager.Application.Abstractions.Email;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Common;
using ProjectManager.Application.Organizations;
using ProjectManager.Domain.Organizations;
using ProjectManager.Infrastructure.Common;
using ProjectManager.Infrastructure.MultiTenancy;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Organizations;

/// <summary>
/// Casos de uso de organizações. Leituras e gravações passam pelos filtros e pela validação da
/// organização ativa do <see cref="ProjectManagerDbContext"/>.
/// </summary>
internal sealed partial class OrganizationService(
    ProjectManagerDbContext dbContext,
    TenantContext tenantContext,
    MembershipDirectory membershipDirectory,
    IEmailSender emailSender,
    OrganizationEmails organizationEmails,
    TimeProvider timeProvider,
    ILogger<OrganizationService> logger) : IOrganizationService
{
    private const string LastOwnerMessage = "A organização precisa de ao menos um proprietário.";
    private const string OwnerChangeMessage = "Somente proprietários podem promover, rebaixar ou remover proprietários.";
    private const string InvalidInvitationMessage = "Convite inválido, expirado ou já utilizado.";

    public async Task<Result<OrganizationSummary>> CreateAsync(Guid userId, OrganizationNameRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var organization = new Organization(request.Name);
        var roles = Role.CreateSystemRoles(organization.Id);
        var ownerRole = roles.Single(role => role.IsOwner);

        // O criador é a prova do vínculo: os papéis e a associação são gravados nesta organização.
        tenantContext.UseOrganization(organization.Id);
        dbContext.Organizations.Add(organization);
        dbContext.Roles.AddRange(roles);
        dbContext.OrganizationMembers.Add(new OrganizationMember(organization.Id, userId, ownerRole));
        await dbContext.SaveChangesAsync(cancellationToken);

        LogOrganizationCreated(logger, organization.Id, userId);
        return Result.Success(new OrganizationSummary(organization.Id, organization.Name, ownerRole.Id, ownerRole.Name));
    }

    public async Task<Result<OrganizationDetails>> GetActiveAsync(CancellationToken cancellationToken)
    {
        var organization = await FindActiveOrganizationAsync(cancellationToken);

        return organization is null
            ? Result.Failure<OrganizationDetails>(ErrorKind.NotFound, "Organização não encontrada.")
            : Result.Success(ToDetails(organization));
    }

    public async Task<Result<OrganizationDetails>> RenameActiveAsync(OrganizationNameRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var organization = await FindActiveOrganizationAsync(cancellationToken);
        if (organization is null)
        {
            return Result.Failure<OrganizationDetails>(ErrorKind.NotFound, "Organização não encontrada.");
        }

        organization.Rename(request.Name);

        return await TrySaveAsync(cancellationToken)
            ? Result.Success(ToDetails(organization))
            : Result.Failure<OrganizationDetails>(ErrorKind.Conflict, "A organização foi alterada por outra pessoa. Recarregue e tente novamente.");
    }

    public async Task<IReadOnlyList<MemberDetails>> ListMembersAsync(CancellationToken cancellationToken) =>
        await QueryMembers().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RoleDetails>> ListRolesAsync(CancellationToken cancellationToken)
    {
        var roles = await dbContext.Roles.AsNoTracking().Include(role => role.Permissions).ToListAsync(cancellationToken);

        return roles
            .OrderBy(role => role.SystemRole ?? SystemRole.Viewer + 1)
            .ThenBy(role => role.Name, StringComparer.Ordinal)
            .Select(role => new RoleDetails(
                role.Id,
                role.Name,
                role.SystemRole?.ToString(),
                role.Permissions.Select(permission => permission.PermissionCode).Order(StringComparer.Ordinal).ToList()))
            .ToList();
    }

    public async Task<Result<MemberDetails>> ChangeMemberRoleAsync(
        ActiveMembership actor,
        Guid userId,
        ChangeMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var member = await dbContext.OrganizationMembers.SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        var newRole = await dbContext.Roles.SingleOrDefaultAsync(role => role.Id == request.RoleId, cancellationToken);
        if (member is null || newRole is null)
        {
            return Result.Failure<MemberDetails>(ErrorKind.NotFound, member is null ? "Membro não encontrado." : "Papel não encontrado.");
        }

        var currentRole = await dbContext.Roles.SingleAsync(role => role.Id == member.RoleId, cancellationToken);
        if ((currentRole.IsOwner || newRole.IsOwner) && !actor.IsOwner)
        {
            return Result.Failure<MemberDetails>(ErrorKind.Forbidden, OwnerChangeMessage);
        }

        if (currentRole.IsOwner && !newRole.IsOwner)
        {
            if (await CountOwnersAsync(cancellationToken) <= 1)
            {
                return Result.Failure<MemberDetails>(ErrorKind.Conflict, LastOwnerMessage);
            }

            await LockOrganizationAsync(cancellationToken);
        }

        member.ChangeRole(newRole);

        if (!await TrySaveAsync(cancellationToken))
        {
            return Result.Failure<MemberDetails>(ErrorKind.Conflict, "Os membros foram alterados por outra pessoa. Recarregue e tente novamente.");
        }

        return Result.Success(await QueryMembers(userId).SingleAsync(cancellationToken));
    }

    public async Task<Result> RemoveMemberAsync(ActiveMembership actor, Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var isSelf = actor.UserId == userId;
        if (!isSelf && !actor.HasPermission(OrganizationPermissions.MembersRemove))
        {
            return Result.Failure(ErrorKind.Forbidden, "Você não tem permissão para remover membros.");
        }

        var member = await dbContext.OrganizationMembers.SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return Result.Failure(ErrorKind.NotFound, "Membro não encontrado.");
        }

        var role = await dbContext.Roles.SingleAsync(r => r.Id == member.RoleId, cancellationToken);
        if (role.IsOwner)
        {
            if (!isSelf && !actor.IsOwner)
            {
                return Result.Failure(ErrorKind.Forbidden, OwnerChangeMessage);
            }

            if (await CountOwnersAsync(cancellationToken) <= 1)
            {
                return Result.Failure(ErrorKind.Conflict, LastOwnerMessage);
            }

            await LockOrganizationAsync(cancellationToken);
        }

        dbContext.OrganizationMembers.Remove(member);

        return await TrySaveAsync(cancellationToken)
            ? Result.Success()
            : Result.Failure(ErrorKind.Conflict, "Os membros foram alterados por outra pessoa. Recarregue e tente novamente.");
    }

    public async Task<Result<InvitationDetails>> CreateInvitationAsync(
        ActiveMembership actor,
        CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var role = await dbContext.Roles.SingleOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);
        if (role is null)
        {
            return Result.Failure<InvitationDetails>(ErrorKind.NotFound, "Papel não encontrado.");
        }

        if (role.IsOwner && !actor.IsOwner)
        {
            return Result.Failure<InvitationDetails>(ErrorKind.Forbidden, OwnerChangeMessage);
        }

        var email = Invitation.NormalizeEmail(request.Email);
        var normalizedForIdentity = email.ToUpperInvariant();
        var alreadyMember = await (
            from member in dbContext.OrganizationMembers
            join user in dbContext.Users on member.UserId equals user.Id
            where user.NormalizedEmail == normalizedForIdentity
            select member.Id).AnyAsync(cancellationToken);
        if (alreadyMember)
        {
            return Result.Failure<InvitationDetails>(ErrorKind.Conflict, "Esta pessoa já é membro da organização.");
        }

        var now = timeProvider.GetUtcNow();
        var openInvitations = await dbContext.Invitations
            .Where(invitation => invitation.Email == email && invitation.AcceptedAt == null && invitation.RevokedAt == null)
            .ToListAsync(cancellationToken);
        if (openInvitations.Any(invitation => invitation.IsPending(now)))
        {
            return Result.Failure<InvitationDetails>(ErrorKind.Conflict, "Já existe um convite pendente para este e-mail.");
        }

        // Convites expirados não bloqueiam um novo convite.
        openInvitations.ForEach(invitation => invitation.Revoke(now));

        var token = OpaqueTokens.Generate();
        var invitation = new Invitation(actor.OrganizationId, email, role, actor.UserId, OpaqueTokens.Hash(token), now);
        dbContext.Invitations.Add(invitation);
        await dbContext.SaveChangesAsync(cancellationToken);

        var organization = await dbContext.Organizations.AsNoTracking().SingleAsync(cancellationToken);
        var inviter = await dbContext.Users.AsNoTracking().SingleAsync(user => user.Id == actor.UserId, cancellationToken);
        await TrySendAsync(organizationEmails.Invitation(email, organization.Name, inviter.DisplayName, role.Name, token), cancellationToken);

        LogInvitationCreated(logger, invitation.Id, actor.OrganizationId, actor.UserId);
        return Result.Success(ToDetails(invitation, role.Name, now));
    }

    public async Task<IReadOnlyList<InvitationDetails>> ListInvitationsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var invitations = await (
            from invitation in dbContext.Invitations.AsNoTracking()
            join role in dbContext.Roles on invitation.RoleId equals role.Id
            orderby invitation.CreatedAt descending
            select new { invitation, role.Name }).ToListAsync(cancellationToken);

        return invitations.Select(item => ToDetails(item.invitation, item.Name, now)).ToList();
    }

    public async Task<Result> RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        var invitation = await dbContext.Invitations.SingleOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invitation is null)
        {
            return Result.Failure(ErrorKind.NotFound, "Convite não encontrado.");
        }

        if (invitation.AcceptedAt is not null)
        {
            return Result.Failure(ErrorKind.Conflict, "Um convite aceito não pode ser revogado.");
        }

        invitation.Revoke(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<AcceptedInvitation>> AcceptInvitationAsync(
        Guid userId,
        AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = timeProvider.GetUtcNow();
        var invitation = await membershipDirectory.FindInvitationByTokenHashAsync(OpaqueTokens.Hash(request.Token), cancellationToken);
        if (invitation is null || !invitation.IsPending(now))
        {
            return Result.Failure<AcceptedInvitation>(ErrorKind.Validation, InvalidInvitationMessage);
        }

        var user = await dbContext.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        if (!user.EmailConfirmed || user.Email is null || !invitation.IsFor(user.Email))
        {
            return Result.Failure<AcceptedInvitation>(ErrorKind.Forbidden, "Este convite foi enviado para outro e-mail.");
        }

        // O token do convite é a prova do vínculo com a organização.
        tenantContext.UseOrganization(invitation.OrganizationId);

        if (await dbContext.OrganizationMembers.AnyAsync(m => m.UserId == userId, cancellationToken))
        {
            return Result.Failure<AcceptedInvitation>(ErrorKind.Conflict, "Você já é membro desta organização.");
        }

        var role = await dbContext.Roles.SingleAsync(r => r.Id == invitation.RoleId, cancellationToken);
        var organization = await dbContext.Organizations.AsNoTracking().SingleAsync(cancellationToken);

        invitation.Accept(userId, now);
        dbContext.OrganizationMembers.Add(new OrganizationMember(invitation.OrganizationId, userId, role));
        await dbContext.SaveChangesAsync(cancellationToken);

        LogInvitationAccepted(logger, invitation.Id, invitation.OrganizationId, userId);
        return Result.Success(new AcceptedInvitation(organization.Id, organization.Name, role.Name));
    }

    private Task<Organization?> FindActiveOrganizationAsync(CancellationToken cancellationToken) =>
        dbContext.Organizations.SingleOrDefaultAsync(cancellationToken);

    // Filtro e ordenação antes da projeção: o EF Core não traduz acesso a membros de um record projetado.
    private IQueryable<MemberDetails> QueryMembers(Guid? userId = null) =>
        from member in dbContext.OrganizationMembers.AsNoTracking()
        join role in dbContext.Roles on member.RoleId equals role.Id
        join user in dbContext.Users on member.UserId equals user.Id
        where userId == null || member.UserId == userId
        orderby user.DisplayName, member.CreatedAt
        select new MemberDetails(member.UserId, user.DisplayName, user.Email!, role.Id, role.Name, member.CreatedAt);

    private Task<int> CountOwnersAsync(CancellationToken cancellationToken) =>
        (from member in dbContext.OrganizationMembers
         join role in dbContext.Roles on member.RoleId equals role.Id
         where role.SystemRole == SystemRole.Owner
         select member.Id).CountAsync(cancellationToken);

    /// <summary>
    /// Marca a organização como alterada: o <c>xmin</c> faz duas operações simultâneas sobre
    /// proprietários conflitarem, em vez de deixarem a organização sem proprietário.
    /// </summary>
    private async Task LockOrganizationAsync(CancellationToken cancellationToken)
    {
        var organization = await dbContext.Organizations.SingleAsync(cancellationToken);
        dbContext.Entry(organization).State = EntityState.Modified;
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private async Task TrySendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (EmailDeliveryException exception)
        {
            LogEmailDeliveryFailed(logger, exception, message.Subject);
        }
    }

    private static OrganizationDetails ToDetails(Organization organization) =>
        new(organization.Id, organization.Name, organization.CreatedAt, organization.UpdatedAt);

    private static InvitationDetails ToDetails(Invitation invitation, string roleName, DateTimeOffset now)
    {
        var status = invitation switch
        {
            { AcceptedAt: not null } => InvitationStatus.Accepted,
            { RevokedAt: not null } => InvitationStatus.Revoked,
            _ when invitation.ExpiresAt <= now => InvitationStatus.Expired,
            _ => InvitationStatus.Pending,
        };

        return new InvitationDetails(invitation.Id, invitation.Email, invitation.RoleId, roleName, status, invitation.ExpiresAt, invitation.CreatedAt);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Organização {OrganizationId} criada pelo usuário {UserId}.")]
    private static partial void LogOrganizationCreated(ILogger logger, Guid organizationId, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Convite {InvitationId} criado na organização {OrganizationId} pelo usuário {UserId}.")]
    private static partial void LogInvitationCreated(ILogger logger, Guid invitationId, Guid organizationId, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Convite {InvitationId} da organização {OrganizationId} aceito pelo usuário {UserId}.")]
    private static partial void LogInvitationAccepted(ILogger logger, Guid invitationId, Guid organizationId, Guid userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao enviar o e-mail '{Subject}'.")]
    private static partial void LogEmailDeliveryFailed(ILogger logger, Exception exception, string subject);
}
