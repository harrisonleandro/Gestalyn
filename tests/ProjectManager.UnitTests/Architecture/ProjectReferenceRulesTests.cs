using System.Xml.Linq;

namespace ProjectManager.UnitTests.Architecture;

/// <summary>
/// Garante que as referências entre projetos declaradas nos .csproj respeitam a arquitetura aprovada
/// (docs/architecture/ARCHITECTURE.md). Web e CLI acessam a API somente por HTTP.
/// </summary>
public sealed class ProjectReferenceRulesTests
{
    public static TheoryData<string, string[]> AllowedReferences => new()
    {
        { "ProjectManager.Domain", [] },
        { "ProjectManager.Application", ["ProjectManager.Domain"] },
        { "ProjectManager.Infrastructure", ["ProjectManager.Application", "ProjectManager.Domain"] },
        { "ProjectManager.API", ["ProjectManager.Application", "ProjectManager.Infrastructure"] },
        { "ProjectManager.Web", [] },
        { "ProjectManager.CLI", [] },
    };

    [Theory]
    [MemberData(nameof(AllowedReferences))]
    public void Project_ReferencesOnlyAllowedProjects(string projectName, string[] allowedReferences)
    {
        var actualReferences = ReadProjectReferences(projectName);

        var forbidden = actualReferences.Except(allowedReferences, StringComparer.Ordinal).ToArray();

        Assert.True(
            forbidden.Length == 0,
            $"{projectName} referencia projetos não permitidos: {string.Join(", ", forbidden)}.");
    }

    [Fact]
    public void SourceProjects_AreAllCoveredByRules()
    {
        var sourceProjects = Directory
            .GetDirectories(Path.Combine(RepositoryRoot.Path, "src"))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var coveredProjects = AllowedReferences
            .Select(row => (string)row.Data.Item1)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(coveredProjects, sourceProjects);
    }

    private static string[] ReadProjectReferences(string projectName)
    {
        var projectPath = Path.Combine(RepositoryRoot.Path, "src", projectName, $"{projectName}.csproj");
        var document = XDocument.Load(projectPath);

        return document
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar)))
            .ToArray();
    }
}
