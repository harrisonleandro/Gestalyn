using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ProjectManager.Infrastructure.Persistence;

internal static class SaveChangesConflicts
{
    /// <summary>
    /// Grava as alterações e devolve <c>false</c> em conflito: concorrência (<c>xmin</c>), unicidade ou
    /// vínculo removido ao mesmo tempo. Qualquer outro erro continua sendo exceção.
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
    }

    public static bool IsConflict(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException
        || exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.SerializationFailure,
        };
}
