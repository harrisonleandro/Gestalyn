using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ProjectManager.Application.Agents;
using ProjectManager.Domain.Agents;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>
/// Idempotência dos POSTs de agentes (ADR-016, D9), numa única transação: trava consultiva pela chave
/// (<c>pg_try_advisory_xact_lock</c>, exceção ao SQL bruto autorizada pelo ADR-016), leitura da resposta guardada,
/// ponto de salvamento antes da operação e, no fim, a resposta gravada junto com a operação. Uma queda antes do commit
/// desfaz as duas coisas.
/// </summary>
internal sealed class AgentIdempotencyStore(ProjectManagerDbContext dbContext, TimeProvider timeProvider) : IAgentIdempotencyStore
{
    private const string OperationSavepoint = "agent_operation";

    public async Task<IIdempotencySession?> BeginAsync(IdempotentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var lockKey = $"{request.Agent.CredentialId:N}:{request.Key}";
            var locked = await dbContext.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({lockKey}, 0)) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!locked)
            {
                await transaction.DisposeAsync();
                return null;
            }

            var now = timeProvider.GetUtcNow();
            var existing = await dbContext.IdempotencyRecords
                .SingleOrDefaultAsync(r => r.CredentialId == request.Agent.CredentialId && r.Key == request.Key, cancellationToken);
            if (existing is not null && existing.IsExpiredAt(now))
            {
                // Expirada: tratada como inexistente e substituída nesta mesma transação.
                dbContext.IdempotencyRecords.Remove(existing);
                await dbContext.SaveChangesAsync(cancellationToken);
                existing = null;
            }

            await transaction.CreateSavepointAsync(OperationSavepoint, cancellationToken);

            var stored = existing is null
                ? null
                : new StoredIdempotentResponse(
                    existing.Method,
                    existing.RouteTemplate,
                    existing.RequestHash,
                    existing.ResponseStatus,
                    existing.ResponseBody,
                    existing.Location,
                    existing.ProjectId);
            return new Session(dbContext, transaction, request, stored, timeProvider);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class Session(
        ProjectManagerDbContext dbContext,
        IDbContextTransaction transaction,
        IdempotentRequest request,
        StoredIdempotentResponse? existing,
        TimeProvider timeProvider) : IIdempotencySession
    {
        private bool _finished;

        public StoredIdempotentResponse? Existing => existing;

        public async Task DiscardOperationAsync(CancellationToken cancellationToken)
        {
            await transaction.RollbackToSavepointAsync(OperationSavepoint, cancellationToken);
            dbContext.ChangeTracker.Clear();
        }

        public async Task CompleteAsync(int statusCode, string body, string? location, Guid? projectId, CancellationToken cancellationToken)
        {
            dbContext.IdempotencyRecords.Add(new IdempotencyRecord(
                request.Agent.OrganizationId,
                request.Agent.AgentId,
                request.Agent.CredentialId,
                request.Key,
                request.Method,
                request.RouteTemplate,
                request.RequestHash,
                statusCode,
                body,
                location,
                projectId,
                timeProvider.GetUtcNow()));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _finished = true;
        }

        public async Task AbandonAsync(CancellationToken cancellationToken)
        {
            if (!_finished)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                _finished = true;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
        }
    }
}
