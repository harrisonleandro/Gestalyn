using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Projects;

/// <summary>
/// Etiqueta de um projeto, em texto livre. É única no projeto sem diferenciar maiúsculas e minúsculas.
/// </summary>
public sealed class ProjectTag : IOrganizationOwned
{
    public const int NameMaxLength = Labels.NameMaxLength;
    public const int MaxPerProject = Labels.MaxPerItem;

    internal ProjectTag(Project project, string name)
    {
        OrganizationId = project.OrganizationId;
        ProjectId = project.Id;
        Name = name;
        NormalizedName = Normalize(name);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private ProjectTag(string name, string normalizedName)
    {
        Name = name;
        NormalizedName = normalizedName;
    }

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; }

    public string NormalizedName { get; private set; }

    /// <summary>Forma usada para unicidade e filtros.</summary>
    public static string Normalize(string name) => Labels.NormalizeName(name);

    internal void ChangeSpelling(string name) => Name = name;
}
