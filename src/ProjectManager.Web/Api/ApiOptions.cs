using System.ComponentModel.DataAnnotations;

namespace ProjectManager.Web.Api;

/// <summary>
/// Configuração de acesso HTTP à ProjectManager.API (seção <c>Api</c>).
/// </summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    [Required]
    public Uri? BaseUrl { get; set; }

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;
}
