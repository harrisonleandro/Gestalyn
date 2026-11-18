using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;
using ProjectManager.Infrastructure.Activity;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>
/// Gestão de agentes, credenciais e acessos a projetos (ADR-016). O token de uma credencial só existe na resposta
/// da criação; o banco e os eventos guardam o hash, o prefixo e os metadados, nunca o token.
/// </summary>
internal sealed class AgentService(
    ProjectManagerDbContext dbContext,
    IProjectAccess projectAccess,
    ActivityRecorder activity,
    IOptions<AgentCredentialOptions> credentialOptions,
    TimeProvider timeProvider) : IAgentService
{
    private const string AgentNotFoundMessage = "Agente não encontrado.";
    private const string DuplicateNameMessage = "Já existe um agente com este nome na organização.";
    private const string ConflictMessage = "O agente foi alterado ao mesmo tempo por outra pessoa. Tente novamente.";

    public async Task<IReadOnlyList<AgentSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        return await dbContext.Agents
            .OrderBy(agent => agent.Name)
            .Select(agent => new AgentSummary(
                agent.Id,
                agent.Name,
                agent.Description,
                agent.Status,
                dbContext.AgentCredentials.Count(c => c.AgentId == agent.Id && c.RevokedAt == null && c.ExpiresAt > now),
                dbContext.AgentProjectAccess.Count(a => a.AgentId == agent.Id),
                agent.CreatedAt,
                agent.DisabledAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<AgentDetails>> GetAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        return agent is null
            ? Result.Failure<AgentDetails>(ErrorKind.NotFound, AgentNotFoundMessage)
            : Result.Success(await ToDetailsAsync(agent, cancellationToken));
    }

    public async Task<Result<AgentDetails>> CreateAsync(ActiveMembership actor, CreateAgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var agent = new Agent(actor.OrganizationId, request.Name, request.Description, actor.UserId);
        if (await NameIsTakenAsync(agent.NormalizedName, exceptAgentId: null, cancellationToken))
        {
            return Result.Failure<AgentDetails>(ErrorKind.Conflict, DuplicateNameMessage);
        }

        dbContext.Agents.Add(agent);
        activity.Record(ActivityActions.AgentCreated, ActivityEntityTypes.Agent, agent.Id, $"Agente '{agent.Name}' criado");

        return await SaveAndDescribeAsync(agent, DuplicateNameMessage, cancellationToken);
    }

    public async Task<Result<AgentDetails>> UpdateAsync(Guid agentId, UpdateAgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var agent = await dbContext.Agents.SingleOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null)
        {
            return Result.Failure<AgentDetails>(ErrorKind.NotFound, AgentNotFoundMessage);
        }

        agent.Update(request.Name ?? agent.Name, request.HasDescription ? request.Description : agent.Description);
        if (await NameIsTakenAsync(agent.NormalizedName, agent.Id, cancellationToken))
        {
            return Result.Failure<AgentDetails>(ErrorKind.Conflict, DuplicateNameMessage);
        }

        var changes = activity.ChangesOf(agent);
        if (!changes.IsEmpty)
        {
            activity.Record(ActivityActions.AgentUpdated, ActivityEntityTypes.Agent, agent.Id, $"Agente '{agent.Name}' alterado", changes: changes);
        }

        return await SaveAndDescribeAsync(agent, DuplicateNameMessage, cancellationToken);
    }

    public async Task<Result<AgentDetails>> DisableAsync(ActiveMembership actor, Guid agentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var agent = await dbContext.Agents.SingleOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null)
        {
            return Result.Failure<AgentDetails>(ErrorKind.NotFound, AgentNotFoundMessage);
        }

        if (agent.IsActive)
        {
            agent.Disable(actor.UserId, timeProvider.GetUtcNow());
            activity.Record(ActivityActions.AgentDisabled, ActivityEntityTypes.Agent, agent.Id, $"Agente '{agent.Name}' desativado");
        }

        return await SaveAndDescribeAsync(agent, ConflictMessage, cancellationToken);
    }

    public async Task<Result<AgentDetails>> EnableAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents.SingleOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null)
        {
            return Result.Failure<AgentDetails>(ErrorKind.NotFound, AgentNotFoundMessage);
        }

        if (!agent.IsActive)
        {
            agent.Enable();
            activity.Record(ActivityActions.AgentEnabled, ActivityEntityTypes.Agent, agent.Id, $"Agente '{agent.Name}' reativado");
        }

        return await SaveAndDescribeAsync(agent, ConflictMessage, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<AgentCredentialDetails>>> ListCredentialsAsync(Guid agentId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Agents.AnyAsync(a => a.Id == agentId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<AgentCredentialDetails>>(ErrorKind.NotFound, AgentNotFoundMessage);
        }

        return Result.Success(await ListCredentialDetailsAsync(agentId, cancellationToken));
    }

    public async Task<Result<CreatedAgentCredential>> CreateCredentialAsync(
        ActiveMembership actor,
        Guid agentId,
        CreateAgentCredentialRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var scopes = AgentScopes.Normalize(request.Scopes, out var scopeError);
        if (scopes is null)
        {
            return Result.Failure<CreatedAgentCredential>(ErrorKind.Validation, scopeError!);
        }

        var options = credentialOptions.Value;
        var lifetimeDays = request.ExpiresInDays ?? options.DefaultLifetimeDays;
        if (lifetimeDays > options.MaxLifetimeDays)
        {
            return Result.Failure<CreatedAgentCredential>(
                ErrorKind.Validation,
                $"A validade deve ser de {AgentCredential.MinLifetimeDays} a {options.MaxLifetimeDays} dias.");
        }

        var now = Timestamps.TruncateToMicroseconds(timeProvider.GetUtcNow());
        NewAgentToken token;
        AgentCredential credential;
        try
        {
            // Serializável: duas criações simultâneas não passam juntas do limite de credenciais ativas.
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            var agent = await dbContext.Agents.SingleOrDefaultAsync(a => a.Id == agentId, cancellationToken);
            if (agent is null)
            {
                return Result.Failure<CreatedAgentCredential>(ErrorKind.NotFound, AgentNotFoundMessage);
            }

            if (!agent.IsActive)
            {
                return Result.Failure<CreatedAgentCredential>(ErrorKind.Conflict, AgentCredential.DisabledAgentMessage);
            }

            var active = await dbContext.AgentCredentials.CountAsync(c => c.AgentId == agentId && c.RevokedAt == null && c.ExpiresAt > now, cancellationToken);
            if (active >= AgentCredential.MaxActivePerAgent)
            {
                return Result.Failure<CreatedAgentCredential>(
                    ErrorKind.Conflict,
                    $"O agente já tem {AgentCredential.MaxActivePerAgent} credenciais ativas. Revogue uma antes de criar outra.");
            }

            token = AgentCredentialTokens.Generate();
            credential = new AgentCredential(agent, request.Name, scopes, token.Hash, token.Prefix, now.AddDays(lifetimeDays), actor.UserId, now);
            dbContext.AgentCredentials.Add(credential);
            activity.Record(
                ActivityActions.AgentCredentialCreated,
                ActivityEntityTypes.AgentCredential,
                credential.Id,
                $"Credencial '{credential.Name}' criada para o agente '{agent.Name}'",
                changes: new ActivityChanges()
                    .Set("agentId", null, agent.Id)
                    .List("scopes", [], credential.Scopes)
                    .Set("expiresAt", null, credential.ExpiresAt));

            if (!await dbContext.TrySaveChangesAsync(cancellationToken))
            {
                return Result.Failure<CreatedAgentCredential>(ErrorKind.Conflict, ConflictMessage);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (SaveChangesConflicts.IsSerializationFailure(exception))
        {
            return Result.Failure<CreatedAgentCredential>(ErrorKind.Conflict, ConflictMessage);
        }
        catch (InvalidOperationException exception) when (SaveChangesConflicts.IsSerializationFailure(exception))
        {
            return Result.Failure<CreatedAgentCredential>(ErrorKind.Conflict, ConflictMessage);
        }

        return Result.Success(new CreatedAgentCredential(ToDetails(credential, now), token.Token));
    }

    public async Task<Result<AgentCredentialDetails>> RevokeCredentialAsync(
        ActiveMembership actor,
        Guid agentId,
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var credential = await dbContext.AgentCredentials.SingleOrDefaultAsync(c => c.Id == credentialId && c.AgentId == agentId, cancellationToken);
        if (credential is null)
        {
            return Result.Failure<AgentCredentialDetails>(ErrorKind.NotFound, "Credencial não encontrada.");
        }

        var now = timeProvider.GetUtcNow();
        if (!credential.IsRevoked)
        {
            credential.Revoke(actor.UserId, now);
            activity.Record(
                ActivityActions.AgentCredentialRevoked,
                ActivityEntityTypes.AgentCredential,
                credential.Id,
                $"Credencial '{credential.Name}' revogada",
                changes: new ActivityChanges().Set("agentId", null, agentId));

            if (!await dbContext.TrySaveChangesAsync(cancellationToken))
            {
                return Result.Failure<AgentCredentialDetails>(ErrorKind.Conflict, ConflictMessage);
            }
        }

        return Result.Success(ToDetails(credential, now));
    }

    public async Task<Result<AgentProjectAccessDetails>> SetProjectAccessAsync(
        ActiveMembership actor,
        Guid agentId,
        Guid projectId,
        SetAgentProjectAccessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        if (!AgentProjectAccess.IsAllowed(request.Role))
        {
            return Result.Failure<AgentProjectAccessDetails>(ErrorKind.Validation, AgentProjectAccess.ManagerNotAllowedMessage);
        }

        var agent = await dbContext.Agents.SingleOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null)
        {
            return Result.Failure<AgentProjectAccessDetails>(ErrorKind.NotFound, AgentNotFoundMessage);
        }

        var denied = await CheckProjectManagementAsync(actor, projectId, cancellationToken);
        if (denied is not null)
        {
            return Result.Failure<AgentProjectAccessDetails>(denied.Kind, denied.Message);
        }

        var project = await dbContext.Projects.SingleAsync(p => p.Id == projectId, cancellationToken);
        var access = await dbContext.AgentProjectAccess.SingleOrDefaultAsync(a => a.AgentId == agentId && a.ProjectId == projectId, cancellationToken);
        if (access is null)
        {
            access = new AgentProjectAccess(agent, project, request.Role, actor.UserId);
            dbContext.AgentProjectAccess.Add(access);
            activity.Record(
                ActivityActions.AgentProjectAccessGranted,
                ActivityEntityTypes.AgentProjectAccess,
                access.Id,
                $"Agente '{agent.Name}' recebeu acesso ao projeto como {request.Role}",
                project.Id,
                changes: new ActivityChanges().Set("agentId", null, agent.Id).Set("role", null, request.Role.ToString()));
        }
        else if (access.Role != request.Role)
        {
            var previous = access.Role;
            access.ChangeRole(request.Role, actor.UserId);
            activity.Record(
                ActivityActions.AgentProjectAccessUpdated,
                ActivityEntityTypes.AgentProjectAccess,
                access.Id,
                $"Papel do agente '{agent.Name}' no projeto alterado para {request.Role}",
                project.Id,
                changes: new ActivityChanges().Set("role", previous.ToString(), request.Role.ToString()));
        }

        if (!await dbContext.TrySaveChangesAsync(cancellationToken))
        {
            return Result.Failure<AgentProjectAccessDetails>(ErrorKind.Conflict, ConflictMessage);
        }

        return Result.Success(new AgentProjectAccessDetails(project.Id, project.Name, access.Role, access.CreatedAt));
    }

    public async Task<Result> RemoveProjectAccessAsync(ActiveMembership actor, Guid agentId, Guid projectId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var agent = await dbContext.Agents.SingleOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null)
        {
            return Result.Failure(ErrorKind.NotFound, AgentNotFoundMessage);
        }

        var denied = await CheckProjectManagementAsync(actor, projectId, cancellationToken);
        if (denied is not null)
        {
            return Result.Failure(denied.Kind, denied.Message);
        }

        var access = await dbContext.AgentProjectAccess.SingleOrDefaultAsync(a => a.AgentId == agentId && a.ProjectId == projectId, cancellationToken);
        if (access is null)
        {
            return Result.Failure(ErrorKind.NotFound, "O agente não tem acesso a este projeto.");
        }

        dbContext.AgentProjectAccess.Remove(access);
        activity.Record(
            ActivityActions.AgentProjectAccessRemoved,
            ActivityEntityTypes.AgentProjectAccess,
            access.Id,
            $"Acesso do agente '{agent.Name}' ao projeto removido",
            projectId,
            changes: new ActivityChanges().Set("agentId", agent.Id, null));

        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success()
            : Result.Failure(ErrorKind.Conflict, ConflictMessage);
    }

    private static AgentCredentialDetails ToDetails(AgentCredential credential, DateTimeOffset now) =>
        new(
            credential.Id,
            credential.Name,
            credential.Prefix,
            credential.Scopes,
            credential.IsRevoked ? AgentCredentialState.Revoked : credential.IsActiveAt(now) ? AgentCredentialState.Active : AgentCredentialState.Expired,
            credential.ExpiresAt,
            credential.CreatedAt,
            credential.RevokedAt,
            credential.LastUsedAt);

    /// <summary>Quem concede ou remove o acesso de um agente precisa gerenciar a equipe daquele projeto (ADR-016).</summary>
    private async Task<ResultError?> CheckProjectManagementAsync(ActiveMembership actor, Guid projectId, CancellationToken cancellationToken)
    {
        var grant = await projectAccess.FindAsync(actor, projectId, cancellationToken);
        if (grant is null)
        {
            return new ResultError(ErrorKind.NotFound, "Projeto não encontrado.");
        }

        return grant.HasPermission(ProjectPermissions.MembersManage)
            ? null
            : new ResultError(ErrorKind.Forbidden, $"Seu acesso a este projeto não concede a permissão '{ProjectPermissions.MembersManage}'.");
    }

    private Task<bool> NameIsTakenAsync(string normalizedName, Guid? exceptAgentId, CancellationToken cancellationToken) =>
        dbContext.Agents.AnyAsync(a => a.NormalizedName == normalizedName && a.Id != exceptAgentId, cancellationToken);

    private async Task<Result<AgentDetails>> SaveAndDescribeAsync(Agent agent, string conflictMessage, CancellationToken cancellationToken) =>
        await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success(await ToDetailsAsync(agent, cancellationToken))
            : Result.Failure<AgentDetails>(ErrorKind.Conflict, conflictMessage);

    private async Task<AgentDetails> ToDetailsAsync(Agent agent, CancellationToken cancellationToken)
    {
        var projects = await (
            from access in dbContext.AgentProjectAccess
            join project in dbContext.Projects on access.ProjectId equals project.Id
            where access.AgentId == agent.Id
            orderby project.Name
            select new AgentProjectAccessDetails(project.Id, project.Name, access.Role, access.CreatedAt)).ToListAsync(cancellationToken);

        return new AgentDetails(
            agent.Id,
            agent.Name,
            agent.Description,
            agent.Status,
            agent.CreatedByUserId,
            agent.CreatedAt,
            agent.DisabledAt,
            await ListCredentialDetailsAsync(agent.Id, cancellationToken),
            projects);
    }

    private async Task<IReadOnlyList<AgentCredentialDetails>> ListCredentialDetailsAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var credentials = await dbContext.AgentCredentials.AsNoTracking()
            .Where(c => c.AgentId == agentId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return credentials.ConvertAll(credential => ToDetails(credential, now));
    }
}
