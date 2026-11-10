using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Organizations;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.ConfigureEntityBase();

        builder.Property(organization => organization.Name)
            .HasMaxLength(Organization.NameMaxLength)
            .IsRequired();
    }
}
