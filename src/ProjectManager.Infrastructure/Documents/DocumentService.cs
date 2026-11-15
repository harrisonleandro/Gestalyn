using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Common;
using ProjectManager.Application.Documents;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Documents;
using ProjectManager.Domain.Projects;
using ProjectManager.Infrastructure.Activity;
using ProjectManager.Infrastructure.Persistence;

namespace ProjectManager.Infrastructure.Documents;

/// <summary>
/// Documentos do projeto (ADR-014, D5). Cada edição grava uma versão nova junto com o documento; a versão esperada
/// e o <c>xmin</c> impedem que uma edição sobrescreva outra.
/// </summary>
internal sealed class DocumentService(ProjectManagerDbContext dbContext, TimeProvider timeProvider, ActivityRecorder activity) : IDocumentService
{
    private const string NotFoundMessage = "Documento não encontrado.";

    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(Guid projectId, bool includeArchived, CancellationToken cancellationToken)
    {
        var documents = dbContext.ProjectDocuments.AsNoTracking().Where(d => d.ProjectId == projectId);
        if (!includeArchived)
        {
            documents = documents.Where(d => d.ArchivedAt == null);
        }

        return await documents
            .OrderBy(d => d.Title)
            .ThenBy(d => d.Id)
            .Select(d => new DocumentSummary(d.Id, d.Title, d.CurrentVersion, d.CreatedByUserId, d.ArchivedAt, d.CreatedAt, d.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<DocumentDetails>> CreateAsync(ProjectAccessGrant access, CreateDocumentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(request);

        var project = await dbContext.Projects.SingleOrDefaultAsync(p => p.Id == access.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result.Failure<DocumentDetails>(ErrorKind.NotFound, "Projeto não encontrado.");
        }

        if (project.IsArchived)
        {
            return Result.Failure<DocumentDetails>(ErrorKind.Conflict, Project.ArchivedMessage);
        }

        var document = new ProjectDocument(project, request.Title, request.Body, access.UserId, timeProvider.GetUtcNow());
        dbContext.ProjectDocuments.Add(document);
        dbContext.ProjectDocumentVersions.Add(document.FirstVersion!);
        activity.Record(ActivityActions.DocumentCreated, ActivityEntityTypes.Document, document.Id, $"Documento '{document.Title}' criado", project.Id);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDetails(document));
    }

    public async Task<Result<DocumentDetails>> GetAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await dbContext.ProjectDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        return document is null
            ? Result.Failure<DocumentDetails>(ErrorKind.NotFound, NotFoundMessage)
            : Result.Success(ToDetails(document));
    }

    public async Task<Result<DocumentDetails>> UpdateAsync(
        ProjectAccessGrant access,
        Guid documentId,
        UpdateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(request);

        var found = await FindWritableAsync(documentId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<DocumentDetails>(found.Error.Kind, found.Error.Message);
        }

        var document = found.Value!;
        if (document.IsArchived)
        {
            return Result.Failure<DocumentDetails>(ErrorKind.Conflict, ProjectDocument.ArchivedMessage);
        }

        ProjectDocumentVersion version;
        try
        {
            version = document.Edit(request.Title, request.Body, request.ExpectedVersion!.Value, access.UserId, timeProvider.GetUtcNow());
        }
        catch (DocumentVersionConflictException exception)
        {
            return Result.Failure<DocumentDetails>(ErrorKind.Conflict, exception.Message);
        }

        dbContext.ProjectDocumentVersions.Add(version);
        activity.Record(
            ActivityActions.DocumentUpdated,
            ActivityEntityTypes.Document,
            document.Id,
            $"Documento '{document.Title}' editado (versão {version.Number})",
            document.ProjectId,
            changes: activity.ChangesOf(document));

        // A versão esperada é conferida na memória; o xmin e o número único da versão resolvem duas edições
        // simultâneas que partiram da mesma versão.
        return await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success(ToDetails(document))
            : Result.Failure<DocumentDetails>(ErrorKind.Conflict, ProjectDocument.VersionConflictMessage);
    }

    public async Task<IReadOnlyList<DocumentVersionSummary>> ListVersionsAsync(Guid documentId, CancellationToken cancellationToken) =>
        await (
            from version in dbContext.ProjectDocumentVersions.AsNoTracking()
            join user in dbContext.Users on version.AuthorId equals user.Id into users
            from user in users.DefaultIfEmpty()
            where version.DocumentId == documentId
            orderby version.Number descending
            select new DocumentVersionSummary(version.Number, version.Title, version.AuthorId, user == null ? null : user.DisplayName, version.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<Result<DocumentVersionDetails>> GetVersionAsync(Guid documentId, int number, CancellationToken cancellationToken)
    {
        var version = await (
            from v in dbContext.ProjectDocumentVersions.AsNoTracking()
            join user in dbContext.Users on v.AuthorId equals user.Id into users
            from user in users.DefaultIfEmpty()
            where v.DocumentId == documentId && v.Number == number
            select new DocumentVersionDetails(v.Number, v.Title, v.Body, v.AuthorId, user == null ? null : user.DisplayName, v.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return version is null
            ? Result.Failure<DocumentVersionDetails>(ErrorKind.NotFound, "Versão não encontrada.")
            : Result.Success(version);
    }

    public async Task<Result<DocumentDetails>> ArchiveAsync(ProjectAccessGrant access, Guid documentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        var found = await FindWritableAsync(documentId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<DocumentDetails>(found.Error.Kind, found.Error.Message);
        }

        var document = found.Value!;
        if (!document.IsArchived)
        {
            document.Archive(access.UserId, timeProvider.GetUtcNow());
            activity.Record(ActivityActions.DocumentArchived, ActivityEntityTypes.Document, document.Id, $"Documento '{document.Title}' arquivado", document.ProjectId);
        }

        return await SaveAsync(document, cancellationToken);
    }

    public async Task<Result<DocumentDetails>> RestoreAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var found = await FindWritableAsync(documentId, cancellationToken);
        if (found.Error is not null)
        {
            return Result.Failure<DocumentDetails>(found.Error.Kind, found.Error.Message);
        }

        var document = found.Value!;
        if (document.IsArchived)
        {
            document.Restore();
            activity.Record(ActivityActions.DocumentRestored, ActivityEntityTypes.Document, document.Id, $"Documento '{document.Title}' restaurado", document.ProjectId);
        }

        return await SaveAsync(document, cancellationToken);
    }

    /// <summary>Documento rastreado para alteração; projeto arquivado é somente leitura.</summary>
    private async Task<Result<ProjectDocument>> FindWritableAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await dbContext.ProjectDocuments.SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<ProjectDocument>(ErrorKind.NotFound, NotFoundMessage);
        }

        return await dbContext.Projects.AnyAsync(p => p.Id == document.ProjectId && p.ArchivedAt != null, cancellationToken)
            ? Result.Failure<ProjectDocument>(ErrorKind.Conflict, Project.ArchivedMessage)
            : Result.Success(document);
    }

    private async Task<Result<DocumentDetails>> SaveAsync(ProjectDocument document, CancellationToken cancellationToken) =>
        await dbContext.TrySaveChangesAsync(cancellationToken)
            ? Result.Success(ToDetails(document))
            : Result.Failure<DocumentDetails>(ErrorKind.Conflict, ProjectDocument.VersionConflictMessage);

    private static DocumentDetails ToDetails(ProjectDocument document) =>
        new(
            document.Id,
            document.ProjectId,
            document.Title,
            document.Body,
            document.CurrentVersion,
            document.CreatedByUserId,
            document.ArchivedAt,
            document.ArchivedByUserId,
            document.CreatedAt,
            document.UpdatedAt);
}
