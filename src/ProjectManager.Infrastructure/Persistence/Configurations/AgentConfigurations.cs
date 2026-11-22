using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;
using ProjectManager.Infrastructure.Identity;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

/// <summary>Agentes (ADR-016). O nome é único na organização, sem diferenciar maiúsculas e minúsculas.</summary>
internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("agents", table => table.HasCheckConstraint("ck_agents_status", "status IN ('Active', 'Disabled')"));
        builder.ConfigureEntityBase();
        builder.Ignore(agent => agent.IsActive);

        builder.Property(agent => agent.Name).HasMaxLength(Agent.NameMaxLength).IsRequired();
        builder.Property(agent => agent.NormalizedName).HasMaxLength(Agent.NameMaxLength).IsRequired();
        builder.Property(agent => agent.Description).HasMaxLength(Agent.DescriptionMaxLength);
        builder.Property(agent => agent.Status).HasConversion<string>().HasMaxLength(16);

        // Alvo das FKs compostas de credenciais, acessos e autorias: nada aponta para um agente de outra organização.
        builder.HasAlternateKey(agent => new { agent.OrganizationId, agent.Id });
        builder.HasIndex(agent => new { agent.OrganizationId, agent.NormalizedName }).IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(agent => agent.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(agent => agent.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(agent => agent.DisabledByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Credenciais (ADR-016): só o SHA-256 do token (único) e o prefixo; os escopos ficam num array de texto com pelo
/// menos um item.
/// </summary>
internal sealed class AgentCredentialConfiguration : IEntityTypeConfiguration<AgentCredential>
{
    private static readonly string KnownScopesSql =
        $"scopes <@ ARRAY[{string.Join(", ", AgentScopes.All.Select(scope => $"'{scope}'"))}]::text[] AND array_position(scopes, NULL) IS NULL";

    public void Configure(EntityTypeBuilder<AgentCredential> builder)
    {
        builder.ToTable("agent_credentials", table =>
        {
            table.HasCheckConstraint("ck_agent_credentials_scopes_not_empty", "cardinality(scopes) > 0");

            // Integridade dos escopos também fora do domínio (SQL direto, manutenção, código futuro com ExecuteUpdate):
            // só os escopos do catálogo e nenhum elemento nulo. O texto vem de AgentScopes.All; um escopo novo muda
            // a restrição e exige uma migração.
            table.HasCheckConstraint("ck_agent_credentials_scopes_known", KnownScopesSql);
            table.HasCheckConstraint("ck_agent_credentials_expires_after_creation", "expires_at > created_at");
        });
        builder.ConfigureEntityBase();
        builder.Ignore(credential => credential.IsRevoked);

        builder.Property(credential => credential.Name).HasMaxLength(AgentCredential.NameMaxLength).IsRequired();
        builder.Property(credential => credential.TokenHash).HasMaxLength(AgentTokens.HashLength).IsFixedLength().IsRequired();
        builder.Property(credential => credential.Prefix).HasMaxLength(AgentTokens.PrefixLength).IsFixedLength().IsRequired();
        builder.Property<string[]>("_scopes").HasColumnName("scopes").IsRequired();
        builder.Ignore(credential => credential.Scopes);

        builder.HasAlternateKey(credential => new { credential.OrganizationId, credential.Id });
        builder.HasIndex(credential => credential.TokenHash).IsUnique();

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(credential => new { credential.OrganizationId, credential.AgentId })
            .HasPrincipalKey(agent => new { agent.OrganizationId, agent.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(credential => credential.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(credential => credential.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Acesso de agentes a projetos (ADR-016): um por agente e projeto, nunca como Gerente.</summary>
internal sealed class AgentProjectAccessConfiguration : IEntityTypeConfiguration<AgentProjectAccess>
{
    public void Configure(EntityTypeBuilder<AgentProjectAccess> builder)
    {
        builder.ToTable("agent_project_access", table =>
            table.HasCheckConstraint("ck_agent_project_access_role", "role IN ('Contributor', 'Viewer')"));
        builder.ConfigureEntityBase();

        builder.Property(access => access.Role).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(access => new { access.AgentId, access.ProjectId }).IsUnique();

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(access => new { access.OrganizationId, access.AgentId })
            .HasPrincipalKey(agent => new { agent.OrganizationId, agent.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(access => new { access.OrganizationId, access.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(access => access.GrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Respostas guardadas de POSTs de agentes (ADR-016, D9): a chave é única por credencial; índice pela expiração para a
/// limpeza de retenção.
/// </summary>
internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.ConfigureEntityBase();

        builder.Property(record => record.Key).HasMaxLength(IdempotencyRecord.KeyMaxLength).IsRequired();
        builder.Property(record => record.Method).HasMaxLength(IdempotencyRecord.MethodMaxLength).IsRequired();
        builder.Property(record => record.RouteTemplate).HasMaxLength(IdempotencyRecord.RouteMaxLength).IsRequired();
        builder.Property(record => record.RequestHash).HasMaxLength(AgentTokens.HashLength).IsFixedLength().IsRequired();
        builder.Property(record => record.ResponseBody).HasMaxLength(IdempotencyRecord.ResponseBodyMaxLength).IsRequired();
        builder.Property(record => record.Location).HasMaxLength(IdempotencyRecord.LocationMaxLength);

        builder.HasIndex(record => new { record.CredentialId, record.Key }).IsUnique();
        builder.HasIndex(record => record.CreatedAt);

        builder.HasOne<AgentCredential>()
            .WithMany()
            .HasForeignKey(record => new { record.OrganizationId, record.CredentialId })
            .HasPrincipalKey(credential => new { credential.OrganizationId, credential.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(record => new { record.OrganizationId, record.AgentId })
            .HasPrincipalKey(agent => new { agent.OrganizationId, agent.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Relatórios de trabalho (ADR-016, D8): imutáveis e permanentes. O gatilho da migração recusa <c>UPDATE</c>,
/// <c>DELETE</c> e <c>TRUNCATE</c>, sem exceção de retenção. A tarefa, se houver, é do mesmo projeto (FK composta).
/// Os estados ficam como retrato (ID e nome do momento), sem FK: o workflow pode mudar depois.
/// </summary>
internal sealed class WorkReportConfiguration : IEntityTypeConfiguration<WorkReport>
{
    public void Configure(EntityTypeBuilder<WorkReport> builder)
    {
        builder.ToTable("work_reports", table =>
        {
            table.HasCheckConstraint(
                "ck_work_reports_tests_non_negative",
                "coalesce(tests_passed, 0) >= 0 AND coalesce(tests_failed, 0) >= 0 AND coalesce(tests_skipped, 0) >= 0");
            table.HasCheckConstraint("ck_work_reports_commit_sha", "commit_sha IS NULL OR commit_sha ~ '^[0-9a-f]{40}([0-9a-f]{24})?$'");
            table.HasCheckConstraint("ck_work_reports_files", $"cardinality(files) <= {WorkReport.MaxFiles} AND array_position(files, NULL) IS NULL");
            table.HasCheckConstraint(
                "ck_work_reports_task_state",
                "(task_id IS NULL) = (task_state_id IS NULL) AND (task_state_id IS NULL) = (task_state_name IS NULL)");
            table.HasCheckConstraint("ck_work_reports_suggested_state", "(suggested_state_id IS NULL) = (suggested_state_name IS NULL)");
        });
        builder.ConfigureEntityBase();

        builder.Property(report => report.Summary).HasMaxLength(WorkReport.SummaryMaxLength).IsRequired();
        builder.Property(report => report.Changes).HasMaxLength(WorkReport.ChangesMaxLength);
        builder.Property<string[]>("_files").HasColumnName("files").IsRequired();
        builder.Ignore(report => report.Files);
        builder.Property(report => report.TestsCommand).HasMaxLength(WorkReport.TestsCommandMaxLength);
        builder.Property(report => report.CommitSha).HasMaxLength(64);
        builder.Property(report => report.CommitRepository).HasMaxLength(WorkReport.CommitRepositoryMaxLength);
        builder.Property(report => report.TaskStateName).HasMaxLength(WorkReport.StateNameMaxLength);
        builder.Property(report => report.SuggestedStateName).HasMaxLength(WorkReport.StateNameMaxLength);
        builder.Property(report => report.RequestId).HasMaxLength(Domain.Activity.ActivityActor.RequestIdMaxLength);

        builder.HasIndex(report => new { report.ProjectId, report.ReceivedAt });
        builder.HasIndex(report => new { report.TaskId, report.ReceivedAt });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(report => new { report.OrganizationId, report.ProjectId })
            .HasPrincipalKey(project => new { project.OrganizationId, project.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Tasks.ProjectTask>()
            .WithMany()
            .HasForeignKey(report => new { report.OrganizationId, report.ProjectId, report.TaskId })
            .HasPrincipalKey(task => new { task.OrganizationId, task.ProjectId, task.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(report => new { report.OrganizationId, report.AgentId })
            .HasPrincipalKey(agent => new { agent.OrganizationId, agent.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AgentCredential>()
            .WithMany()
            .HasForeignKey(report => new { report.OrganizationId, report.CredentialId })
            .HasPrincipalKey(credential => new { credential.OrganizationId, credential.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Auditoria das requisições de agentes (ADR-016, D10). O gatilho da migração recusa <c>UPDATE</c> e
/// <c>TRUNCATE</c> sempre e só aceita o <c>DELETE</c> de linhas com mais de 180 dias (D12).
/// </summary>
internal sealed class AgentOperationConfiguration : IEntityTypeConfiguration<AgentOperation>
{
    public void Configure(EntityTypeBuilder<AgentOperation> builder)
    {
        builder.ToTable("agent_operations", table =>
            table.HasCheckConstraint("ck_agent_operations_duration", "duration_ms >= 0"));
        builder.ConfigureEntityBase();

        builder.Property(operation => operation.HttpMethod).HasMaxLength(AgentOperation.MethodMaxLength).IsRequired();
        builder.Property(operation => operation.RouteTemplate).HasMaxLength(AgentOperation.RouteTemplateMaxLength).IsRequired();
        builder.Property(operation => operation.AuthorizationResult).HasConversion<string>().HasMaxLength(32);
        builder.Property(operation => operation.DenialCode).HasMaxLength(AgentOperation.DenialCodeMaxLength);
        builder.Property(operation => operation.CorrelationId).HasMaxLength(AgentOperation.CorrelationIdMaxLength);
        builder.Property(operation => operation.ClientOrigin).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(operation => new { operation.AgentId, operation.OccurredAt });
        builder.HasIndex(operation => new { operation.OrganizationId, operation.OccurredAt });
        builder.HasIndex(operation => operation.OccurredAt);

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(operation => new { operation.OrganizationId, operation.AgentId })
            .HasPrincipalKey(agent => new { agent.OrganizationId, agent.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AgentCredential>()
            .WithMany()
            .HasForeignKey(operation => new { operation.OrganizationId, operation.CredentialId })
            .HasPrincipalKey(credential => new { credential.OrganizationId, credential.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Contadores do rate limiting (D11): gravados só pelo upsert atômico; apagados depois de 1 dia.</summary>
internal sealed class AgentRateLimitCounterConfiguration : IEntityTypeConfiguration<AgentRateLimitCounter>
{
    public void Configure(EntityTypeBuilder<AgentRateLimitCounter> builder)
    {
        builder.ToTable("agent_rate_limit_counters", table =>
            table.HasCheckConstraint("ck_agent_rate_limit_counters_count", "request_count > 0"));
        builder.HasKey(counter => new { counter.CredentialId, counter.WindowStart });
        builder.HasIndex(counter => counter.WindowStart);

        builder.HasOne<AgentCredential>()
            .WithMany()
            .HasForeignKey(counter => new { counter.OrganizationId, counter.CredentialId })
            .HasPrincipalKey(credential => new { credential.OrganizationId, credential.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
