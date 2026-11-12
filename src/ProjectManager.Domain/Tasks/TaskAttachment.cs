using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Tasks;

/// <summary>
/// Arquivo anexado a uma tarefa. O conteúdo fica no armazenamento de arquivos sob uma chave derivada dos
/// identificadores (nunca do nome enviado); aqui ficam os metadados (ADR-011).
/// </summary>
public sealed class TaskAttachment : Entity, IOrganizationOwned, IAuditable
{
    public const int MaxPerTask = 20;
    public const int ContentTypeMaxLength = 128;
    public const int StorageKeyMaxLength = 200;

    public TaskAttachment(ProjectTask task, string fileName, AttachmentFileType type, long sizeBytes, string sha256, Guid uploadedByUserId)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sizeBytes, AttachmentFiles.MaxSizeBytes);
        task.EnsureNotArchived();

        OrganizationId = task.OrganizationId;
        ProjectId = task.ProjectId;
        TaskId = task.Id;
        FileName = Guard.RequiredText(fileName, AttachmentFiles.FileNameMaxLength, nameof(fileName));
        ContentType = type.ContentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256.Length == 64 ? sha256 : throw new ArgumentException("Hash SHA-256 inválido.", nameof(sha256));
        UploadedByUserId = Guard.RequiredId(uploadedByUserId, nameof(uploadedByUserId));
        StorageKey = $"attachments/{OrganizationId:N}/{Id:N}";
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private TaskAttachment(string fileName, string contentType, string sha256, string storageKey)
    {
        FileName = fileName;
        ContentType = contentType;
        Sha256 = sha256;
        StorageKey = storageKey;
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    /// <summary>Nome original, limpo por <see cref="AttachmentFiles.SanitizeFileName"/>.</summary>
    public string FileName { get; private set; }

    /// <summary>Tipo definido pelo servidor a partir da lista de tipos permitidos, nunca o enviado pelo cliente.</summary>
    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    /// <summary>Chave no armazenamento de arquivos: só letras, números e barras, sem nada vindo do cliente.</summary>
    public string StorageKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
