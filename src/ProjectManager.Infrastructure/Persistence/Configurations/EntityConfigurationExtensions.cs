using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectManager.Domain.Common;

namespace ProjectManager.Infrastructure.Persistence.Configurations;

internal static class EntityConfigurationExtensions
{
    /// <summary>
    /// Propriedade sombra de concorrência otimista, mapeada para a coluna de sistema <c>xmin</c> do
    /// PostgreSQL (não existe como coluna física; muda a cada atualização da linha).
    /// </summary>
    public const string ConcurrencyTokenPropertyName = "Version";

    /// <summary>
    /// Chave UUID gerada no domínio e token de concorrência <c>xmin</c>.
    /// </summary>
    public static EntityTypeBuilder<TEntity> ConfigureEntityBase<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : Entity
    {
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();

        builder.Property<uint>(ConcurrencyTokenPropertyName)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion();

        return builder;
    }
}
