namespace ProjectManager.UnitTests.Architecture;

/// <summary>
/// O filtro por organização só pode ser ignorado nos pontos autorizados (AGENTS.md, ADR-008 e ADR-016). Um uso novo
/// de <c>IgnoreQueryFilters</c> no código de produção faz este teste falhar até ser justificado num ADR.
/// </summary>
public sealed class TenantFilterBypassTests
{
    private static readonly HashSet<string> AllowedFiles = new(StringComparer.Ordinal)
    {
        // ADR-008: associação do usuário do token e convite pelo hash.
        "src/ProjectManager.Infrastructure/Organizations/MembershipDirectory.cs",

        // ADR-016: credencial de agente pelo hash do token.
        "src/ProjectManager.Infrastructure/Agents/AgentCredentialDirectory.cs",
    };

    [Fact]
    public void IgnoreQueryFilters_IsUsedOnlyInAuthorizedFiles()
    {
        var sourceRoot = Path.Combine(RepositoryRoot.Path, "src");
        var users = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsGenerated(file))
            .Where(file => File.ReadAllText(file).Contains("IgnoreQueryFilters", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepositoryRoot.Path, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(AllowedFiles.Order(StringComparer.Ordinal), users);
    }

    private static bool IsGenerated(string file)
    {
        var normalized = file.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.Contains("/Migrations/", StringComparison.Ordinal);
    }
}
