using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;

namespace ProjectManager.Domain.Documents;

/// <summary>
/// Documento do projeto em Markdown (ADR-014, D5). Cada edição cria uma versão imutável; o documento guarda a
/// versão atual para leitura rápida. Arquivado, fica somente leitura até ser restaurado.
/// </summary>
public sealed class ProjectDocument : Entity, IOrganizationOwned, IAuditable
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 200_000;
    public const string ArchivedMessage = "O documento está arquivado e é somente leitura.";
    public const string VersionConflictMessage = "O documento foi alterado por outra pessoa desde a versão que você abriu. Recarregue e tente novamente.";

    public ProjectDocument(Project project, string title, string body, Guid authorId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.EnsureNotArchived();

        OrganizationId = project.OrganizationId;
        ProjectId = project.Id;
        CreatedByUserId = Guard.RequiredId(authorId, nameof(authorId));
        Title = Guard.RequiredText(title, TitleMaxLength, nameof(title));
        Body = ValidBody(body);
        CurrentVersion = 1;
        FirstVersion = new ProjectDocumentVersion(this, CurrentVersion, authorId, now);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private ProjectDocument(string title, string body)
    {
        Title = title;
        Body = body;
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    /// <summary>Número da versão atual, a partir de 1.</summary>
    public int CurrentVersion { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public Guid? ArchivedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    /// <summary>Versão 1, criada junto com o documento. Só existe na instância criada agora.</summary>
    public ProjectDocumentVersion? FirstVersion { get; }

    /// <summary>
    /// Edita o documento a partir da versão que o autor abriu. Uma versão diferente da atual é conflito: a edição
    /// de outra pessoa não é sobrescrita.
    /// </summary>
    public ProjectDocumentVersion Edit(string title, string body, int expectedVersion, Guid authorId, DateTimeOffset now)
    {
        EnsureNotArchived();
        if (expectedVersion != CurrentVersion)
        {
            throw new DocumentVersionConflictException(VersionConflictMessage);
        }

        Title = Guard.RequiredText(title, TitleMaxLength, nameof(title));
        Body = ValidBody(body);
        CurrentVersion++;
        return new ProjectDocumentVersion(this, CurrentVersion, authorId, now);
    }

    public void Archive(Guid userId, DateTimeOffset now)
    {
        ArchivedAt ??= Timestamps.TruncateToMicroseconds(now);
        ArchivedByUserId ??= Guard.RequiredId(userId, nameof(userId));
    }

    public void Restore()
    {
        ArchivedAt = null;
        ArchivedByUserId = null;
    }

    public void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new InvalidOperationException(ArchivedMessage);
        }
    }

    private static string ValidBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return body.Length > BodyMaxLength
            ? throw new ArgumentException($"O documento deve ter no máximo {BodyMaxLength} caracteres.", nameof(body))
            : body;
    }
}

/// <summary>Versão imutável de um documento: o título e o corpo como ficaram naquela edição.</summary>
public sealed class ProjectDocumentVersion : Entity, IOrganizationOwned
{
    internal ProjectDocumentVersion(ProjectDocument document, int number, Guid authorId, DateTimeOffset createdAt)
    {
        OrganizationId = document.OrganizationId;
        ProjectId = document.ProjectId;
        DocumentId = document.Id;
        Number = number;
        Title = document.Title;
        Body = document.Body;
        AuthorId = Guard.RequiredId(authorId, nameof(authorId));
        CreatedAt = Timestamps.TruncateToMicroseconds(createdAt);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private ProjectDocumentVersion(string title, string body)
    {
        Title = title;
        Body = body;
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid DocumentId { get; private set; }

    public int Number { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public Guid AuthorId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>A edição partiu de uma versão que já não é a atual. A API responde 409.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Sempre criada com a mensagem do conflito.")]
public sealed class DocumentVersionConflictException(string message) : InvalidOperationException(message);
