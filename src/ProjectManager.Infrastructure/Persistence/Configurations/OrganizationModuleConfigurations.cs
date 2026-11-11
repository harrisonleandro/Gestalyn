using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Organizations;
using ProjectManager.Infrastructure.Identity;
using ProjectManager.Infrastructure.Organizations;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

internal sealed class PermissionDefinitionConfiguration : IEntityTypeConfiguration<PermissionDefinition>
{
    public void Configure(EntityTypeBuilder<PermissionDefinition> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(permission => permission.Code);
        builder.Property(permission => permission.Code).HasMaxLength(OrganizationPermissions.CodeMaxLength);
        builder.Property(permission => permission.Description).HasMaxLength(200).IsRequired();

        builder.HasData(OrganizationPermissions.Catalog.Select(entry => new PermissionDefinition(entry.Key, entry.Value)));
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.ConfigureEntityBase();
        builder.Ignore(role => role.IsOwner);

        builder.Property(role => role.Name).HasMaxLength(Role.NameMaxLength).IsRequired();
        builder.Property(role => role.SystemRole).HasConversion<string>().HasMaxLength(32);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(role => role.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Alvo das FKs compostas de membros, convites e permissões: impede papéis de outra organização.
        builder.HasAlternateKey(role => new { role.OrganizationId, role.Id });

        // Um papel de sistema de cada tipo por organização.
        builder.HasIndex(role => new { role.OrganizationId, role.SystemRole })
            .IsUnique()
            .HasFilter("system_role IS NOT NULL");

        builder.HasMany(role => role.Permissions)
            .WithOne()
            .HasForeignKey(permission => new { permission.OrganizationId, permission.RoleId })
            .HasPrincipalKey(role => new { role.OrganizationId, role.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(role => role.Permissions)
            .HasField("_permissions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(permission => new { permission.RoleId, permission.PermissionCode });
        builder.Property(permission => permission.PermissionCode).HasMaxLength(OrganizationPermissions.CodeMaxLength);

        builder.HasOne<PermissionDefinition>()
            .WithMany()
            .HasForeignKey(permission => permission.PermissionCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.ToTable("organization_members");
        builder.ConfigureEntityBase();

        // Uma associação por usuário e organização. É também o alvo da FK composta das equipes de
        // projeto: só membros da organização entram nelas, e saem quando deixam a organização.
        builder.HasAlternateKey(member => new { member.OrganizationId, member.UserId });
        builder.HasIndex(member => member.UserId);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(member => new { member.OrganizationId, member.RoleId })
            .HasPrincipalKey(role => new { role.OrganizationId, role.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");
        builder.ConfigureEntityBase();

        builder.Property(invitation => invitation.Email).HasMaxLength(Invitation.EmailMaxLength).IsRequired();
        builder.Property(invitation => invitation.TokenHash).HasMaxLength(64).IsRequired();

        builder.HasIndex(invitation => invitation.TokenHash).IsUnique();

        // No máximo um convite em aberto por e-mail em cada organização.
        builder.HasIndex(invitation => new { invitation.OrganizationId, invitation.Email })
            .IsUnique()
            .HasFilter("accepted_at IS NULL AND revoked_at IS NULL");

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(invitation => new { invitation.OrganizationId, invitation.RoleId })
            .HasPrincipalKey(role => new { role.OrganizationId, role.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(invitation => invitation.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(invitation => invitation.AcceptedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
