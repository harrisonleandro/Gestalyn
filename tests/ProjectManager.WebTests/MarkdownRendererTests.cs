using ProjectManager.Web.Markdown;

namespace ProjectManager.WebTests;

/// <summary>Markdown seguro (ADR-015, D6): o conteúdo do usuário nunca vira HTML ou script.</summary>
public sealed class MarkdownRendererTests
{
    private readonly MarkdownRenderer _renderer = new();

    [Fact]
    public void FormattingIsKept()
    {
        var html = _renderer.Render("# Título\n\n**negrito** e `código`\n\n| a | b |\n|---|---|\n| 1 | 2 |").Value;

        Assert.Contains("<h1", html, StringComparison.Ordinal);
        Assert.Contains("<strong>negrito</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<code>código</code>", html, StringComparison.Ordinal);
        Assert.Contains("<table>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<iframe src=\"https://exemplo.com\"></iframe>")]
    [InlineData("texto <b onclick=\"alert(1)\">clique</b>")]
    public void RawHtml_IsEscaped(string markdown)
    {
        var html = _renderer.Render(markdown).Value;

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b ", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JavaScript:alert(1))")]
    [InlineData("[x](java\tscript:alert(1))")]
    [InlineData("[x](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("[x](vbscript:msgbox)")]
    [InlineData("<javascript:alert(1)>")]
    public void DangerousLinks_PointNowhere(string markdown)
    {
        var html = _renderer.Render(markdown).Value;

        // O texto pode até aparecer, escapado; o que não pode existir é um link (ou outro atributo) apontando para ele.
        var hrefs = System.Text.RegularExpressions.Regex.Matches(html, "(?:href|src)=\"([^\"]*)\"").Select(match => match.Groups[1].Value).ToList();
        Assert.All(hrefs, href => Assert.Equal("#", href));
    }

    [Fact]
    public void SafeLinks_KeepTheirTarget_WithRelNoopener_AndImagesBecomeLinks()
    {
        var html = _renderer.Render("[site](https://exemplo.com) [e-mail](mailto:a@exemplo.com) [local](/o/x) ![foto](https://rastreio.exemplo/p.png)").Value;

        Assert.Contains("href=\"https://exemplo.com\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:a@exemplo.com\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/o/x\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains(">foto</a>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("/caminho", true)]
    [InlineData("#ancora", true)]
    [InlineData("relativo/arquivo.md", true)]
    [InlineData("https://exemplo.com", true)]
    [InlineData("HTTP://exemplo.com", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData(" javascript:alert(1)", false)]
    [InlineData("ftp://exemplo.com", false)]
    public void IsSafe_AllowsOnlyKnownSchemesAndRelativePaths(string? url, bool expected) =>
        Assert.Equal(expected, MarkdownRenderer.IsSafe(url));
}
