using System.Net;
using ProjectManager.Web.Api;
using ProjectManager.Web.Components.Shared;
using ProjectManager.WebTests.Infrastructure;

namespace ProjectManager.WebTests;

/// <summary>Estados de carregamento, erro e vazio, e o progresso pela regra da API (ADR-015, D1).</summary>
public sealed class StateComponentTests : IDisposable
{
    private readonly WebTestContext _context = new();

    [Fact]
    public void Loading_IsAnnouncedPolitely()
    {
        var cut = _context.Render<States>(parameters => parameters.Add(p => p.Ready, false));

        var status = cut.Find("[role=status]");
        Assert.Equal("polite", status.GetAttribute("aria-live"));
        Assert.Contains("Carregando", status.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "Não encontrado", "não existe ou você não tem acesso")]
    [InlineData(HttpStatusCode.Forbidden, "Sem permissão", "Detalhe da API")]
    [InlineData(HttpStatusCode.Unauthorized, "Sessão expirada", "Detalhe da API")]
    [InlineData(null, "Serviço indisponível", "Detalhe da API")]
    public void Errors_ShowWhatHappened_InsteadOfTheContent(HttpStatusCode? status, string title, string detail)
    {
        var cut = _context.Render<States>(parameters => parameters
            .Add(p => p.Error, ApiResult.Failure(status, "Detalhe da API"))
            .Add(p => p.Ready, true)
            .AddChildContent("<p id=\"conteudo\">dados</p>"));

        var alert = cut.Find("[role=alert]");
        Assert.Contains(title, alert.TextContent, StringComparison.Ordinal);
        Assert.Contains(detail, alert.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#conteudo"));
    }

    [Fact]
    public void Progress_WithoutTasks_SaysNoData_NeverZero()
    {
        var cut = _context.Render<ProgressSummary>(parameters => parameters
            .Add(p => p.Value, new Progress(new CategoryCounts(0, 0, 0, 0, 2, 2), null, "doneOverNotCanceled")));

        Assert.Equal("Sem dados", cut.Find(".pm-progress-value").TextContent);
        Assert.Equal("Sem dados", cut.Find("[role=progressbar]").GetAttribute("aria-valuetext"));
        Assert.Contains("Canceladas 2", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Progress_ShowsThePercentAndTheCountsThatExplainIt()
    {
        var cut = _context.Render<ProgressSummary>(parameters => parameters
            .Add(p => p.Value, new Progress(new CategoryCounts(1, 1, 1, 2, 1, 6), 40.0, "doneOverNotCanceled")));

        Assert.Contains("40%", cut.Find(".pm-progress-value").TextContent, StringComparison.Ordinal);
        Assert.Contains("2 de 5 concluídas", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("40", cut.Find("[role=progressbar]").GetAttribute("aria-valuenow"));
    }

    [Fact]
    public void StateBadge_AlwaysShowsTheName_NotOnlyTheColor()
    {
        var cut = _context.Render<StateBadge>(parameters => parameters.Add(p => p.Name, "Em revisão").Add(p => p.Category, StateCategory.InProgress));

        Assert.Equal("Em revisão", cut.Find(".pm-badge").TextContent);
        Assert.Contains("pm-cat-inprogress", cut.Find(".pm-badge").ClassName, StringComparison.Ordinal);
    }

    public void Dispose() => _context.Dispose();
}
