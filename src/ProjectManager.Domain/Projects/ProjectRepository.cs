using System.Diagnostics.CodeAnalysis;
using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Projects;

/// <summary>Provedor do repositório, inferido do host da URL.</summary>
public enum RepositoryProvider
{
    GitHub,
    GitLab,
    Bitbucket,
    Other,
}

/// <summary>
/// Repositório associado a um projeto. Nesta fase guarda só metadados: nenhuma chamada ao provedor e
/// nenhuma credencial (ADR-010).
/// </summary>
public sealed class ProjectRepository : Entity, IOrganizationOwned, IAuditable
{
    public const int UrlMaxLength = 500;
    public const int NameMaxLength = 100;
    public const int MaxPerProject = 20;

    internal ProjectRepository(Project project, Uri url, string? name)
    {
        OrganizationId = project.OrganizationId;
        ProjectId = project.Id;
        Url = url.AbsoluteUri;
        Provider = ProviderOf(url);
        Name = Guard.OptionalText(name, NameMaxLength, nameof(name));
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Construtor usado pelo EF Core na materialização.")]
    private ProjectRepository(string url) => Url = url;

    public Guid OrganizationId { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>URL normalizada: <c>https</c>, host em minúsculas, sem <c>.git</c> nem barra final.</summary>
    public string Url { get; private set; }

    public string? Name { get; private set; }

    public RepositoryProvider Provider { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Valida e normaliza a URL do repositório. Só aceita <c>https</c>, sem credenciais, consulta ou
    /// fragmento, para que nada sensível seja gravado por engano.
    /// </summary>
    public static bool TryNormalizeUrl(string? value, [NotNullWhen(true)] out Uri? url, [NotNullWhen(false)] out string? error)
    {
        url = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(parsed.Host))
        {
            error = "Informe uma URL https válida para o repositório.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            error = "A URL do repositório não pode conter credenciais, parâmetros de consulta ou fragmento.";
            return false;
        }

        var path = parsed.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        // O caminho já vem escapado do Uri original; montar o texto evita escapá-lo de novo.
        var authority = parsed.IsDefaultPort ? parsed.Host.ToLowerInvariant() : $"{parsed.Host.ToLowerInvariant()}:{parsed.Port}";
        var normalized = new Uri($"https://{authority}{path}", UriKind.Absolute);
        if (normalized.AbsoluteUri.Length > UrlMaxLength)
        {
            error = $"A URL do repositório deve ter no máximo {UrlMaxLength} caracteres.";
            return false;
        }

        url = normalized;
        return true;
    }

    private static RepositoryProvider ProviderOf(Uri url) => url.Host switch
    {
        "github.com" or "www.github.com" => RepositoryProvider.GitHub,
        "gitlab.com" or "www.gitlab.com" => RepositoryProvider.GitLab,
        "bitbucket.org" or "www.bitbucket.org" => RepositoryProvider.Bitbucket,
        _ => RepositoryProvider.Other,
    };
}
