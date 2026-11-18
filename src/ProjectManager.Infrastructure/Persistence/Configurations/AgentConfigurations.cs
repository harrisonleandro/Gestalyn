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
