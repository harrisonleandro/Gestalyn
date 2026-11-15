using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace ProjectManager.Web.Markdown;

/// <summary>
/// Renderização segura do Markdown escrito pelos usuários (ADR-015, D6): o HTML cru é escapado, links só para
/// <c>http</c>, <c>https</c>, <c>mailto</c> e caminhos relativos, e imagens viram links (a CSP não carrega imagens de
/// fora, e carregá-las revelaria quem abriu a página). Links externos levam <c>rel="noopener noreferrer nofollow"</c>.
/// </summary>
public sealed class MarkdownRenderer
{
    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];

    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseReferralLinks("noopener", "noreferrer", "nofollow")
        .Build();

    public MarkupString Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new MarkupString(string.Empty);
        }

        var document = Markdig.Markdown.Parse(markdown, _pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            link.IsImage = false;
            if (!IsSafe(link.Url))
            {
                link.Url = "#";
            }
        }

        foreach (var link in document.Descendants<AutolinkInline>())
        {
            if (!IsSafe(link.Url))
            {
                link.Url = "#";
            }
        }

        return new MarkupString(document.ToHtml(_pipeline));
    }

    /// <summary>Esquemas permitidos, ou um caminho relativo (sem esquema).</summary>
    internal static bool IsSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        // Caracteres de controle e espaços antes do esquema enganam alguns navegadores ("java\tscript:").
        var compact = new string(url.Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c)).ToArray());
        var colon = compact.IndexOf(':', StringComparison.Ordinal);
        var firstSeparator = compact.IndexOfAny(['/', '?', '#']);
        if (colon < 0 || (firstSeparator >= 0 && firstSeparator < colon))
        {
            return true;
        }

        var scheme = compact[..colon];
        return AllowedSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase);
    }
}
