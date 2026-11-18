using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ProjectManager.Infrastructure.Persistence;

internal static class SaveChangesConflicts
{
    /// <summary>
    /// Grava as alterações e devolve <c>false</c> em conflito: concorrência (<c>xmin</c>), unicidade, vínculo removido
    /// ao mesmo tempo ou transação serializável recusada. Qualquer outro erro continua sendo exceção.
    /// </summary>
    public static async Task<bool> TrySaveChangesAsync(this DbContext dbContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsConflict(exception))
        {
            return false;
        }
        catch (InvalidOperationException exception) when (exception.InnerException is DbUpdateException inner && IsConflict(inner))
        {
            // O provedor do Npgsql embrulha falhas transitórias (como a 40001) nesta exceção.
            return false;
        }
    }

    public static bool IsConflict(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException
        || exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.SerializationFailure,
        };

    /// <summary>
    /// Transação serializável recusada pelo PostgreSQL (40001), lançada diretamente ou embrulhada pelo EF Core e pelo
    /// provedor do Npgsql. Quem a recebe responde 409, para nova tentativa.
    /// </summary>
    public static bool IsSerializationFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
            {
                return true;
            }
        }

        return false;
    }
}
