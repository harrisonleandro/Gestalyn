using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Infrastructure.Identity;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("users");
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.DisplayName)
            .HasMaxLength(AuthLimits.DisplayNameMaxLength)
            .IsRequired();

        // O Identity cria índices com nomes próprios e o de e-mail não é único; aqui o banco também
        // garante e-mail único (protege contra cadastros simultâneos).
        builder.HasIndex(user => user.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ix_users_normalized_email");

        builder.HasIndex(user => user.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("ix_users_normalized_user_name");
    }
}

internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("user_claims");
}

internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("user_logins");
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("user_tokens");
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions");
        builder.ConfigureEntityBase();

        builder.Property(session => session.RevocationReason)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserRefreshTokenConfiguration : IEntityTypeConfiguration<UserRefreshToken>
{
    public void Configure(EntityTypeBuilder<UserRefreshToken> builder)
    {
        builder.ToTable("user_refresh_tokens");
        builder.ConfigureEntityBase();

        // SHA-256 em hexadecimal: 64 caracteres.
        builder.Property(token => token.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(token => token.TokenHash).IsUnique();

        // Cadeia de rotação (ADR-012): cada token tem no máximo um sucessor, e cada sucessor um antecessor.
        // NO ACTION: a exclusão da sessão apaga a cadeia inteira no mesmo comando.
        builder.HasOne<UserRefreshToken>()
            .WithOne()
            .HasForeignKey<UserRefreshToken>(token => token.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(token => token.ReplayCount).HasDefaultValue(0);

        builder.HasOne(token => token.Session)
            .WithMany()
            .HasForeignKey(token => token.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
