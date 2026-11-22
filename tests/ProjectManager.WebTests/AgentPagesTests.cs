using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ProjectManager.Web.Api;
using ProjectManager.Web.Components.Pages.Agents;
using ProjectManager.Web.Components.Shared;
using ProjectManager.WebTests.Infrastructure;

namespace ProjectManager.WebTests;

/// <summary>
/// Telas de agentes (ADR-016, D13): só aparecem com <c>agents.manage</c> devolvida pela API; o token de uma credencial
/// aparece uma única vez, na resposta do formulário de criação, sem navegação, sem log e sem voltar depois.
/// </summary>
public sealed class AgentPagesTests : IDisposable
{
    private static readonly Guid AgentId = Guid.Parse("01a1df32-0000-7000-8000-000000000101");
    private static readonly Guid ActiveCredential = Guid.Parse("01a1df32-0000-7000-8000-000000000102");
    private static readonly Guid RevokedCredential = Guid.Parse("01a1df32-0000-7000-8000-000000000103");
    private static readonly Guid OtherProject = Guid.Parse("01a1df32-0000-7000-8000-000000000104");

    // Valor fictício, só para procurar na tela e nos logs; não é um token real.
    private const string FakeToken = "gsk_valor_ficticio_de_teste_9f3c2a71";

    private readonly WebTestContext _context = new();

    private static string AgentsPath => $"organizations/{WebTestContext.OrganizationId}/agents";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrganizationMenu_ShowsAgentsOnlyWithAgentsManage(bool canManage)
    {
        var membership = new MyMembership(WebTestContext.OrganizationId, "Riuk Labs", Guid.NewGuid(), "Membro", false,
            canManage ? [Permissions.AgentsManage] : []);

        var cut = _context.Render<OrgNav>(parameters => parameters.Add(p => p.Membership, membership));

        Assert.Equal(canManage, cut.Markup.Contains("Agentes", StringComparison.Ordinal));
    }

    [Fact]
    public void AgentList_WithoutThePermission_ShowsForbidden_AndAsksNothingAboutAgents()
    {
        _context.Membership(Permissions.MembersRead);

        var cut = RenderList();

        cut.WaitForAssertion(() => Assert.Contains("Sem permissão", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(_context.Api.Calls, call => call.Contains("/agents", StringComparison.Ordinal));
        Assert.DoesNotContain("Novo agente", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentList_ShowsTheAgentsTheApiReturns_AndOffersCreation()
    {
        _context.Membership(Permissions.AgentsManage);
        _context.Api.Json(HttpMethod.Get, AgentsPath, new[]
        {
            new AgentSummary(AgentId, "Assistente de código", "Abre tarefas", AgentStatus.Active, 1, 2, DateTimeOffset.UnixEpoch, null),
            new AgentSummary(Guid.NewGuid(), "Antigo", null, AgentStatus.Disabled, 0, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
        });

        var cut = RenderList();

        cut.WaitForAssertion(() => Assert.Contains("Assistente de código", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Desativado", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"a[href$='/agentes/{AgentId}']"));
        Assert.Contains("Criar agente", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentDetails_ListsCredentialsByPrefix_WithoutAnyToken_AndConfirmsDestructiveActions()
    {
        StubAgent();

        var cut = RenderDetails();

        cut.WaitForAssertion(() => Assert.Contains("gsk_ab12cd34…", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#agent-token"));
        Assert.DoesNotContain("Copie o token agora", cut.Markup, StringComparison.Ordinal);

        // Só a credencial ativa pode ser revogada, e com confirmação; a revogada mostra a data.
        Assert.NotNull(cut.Find($"form[data-confirm*='Revogar a credencial CI']"));
        Assert.Empty(cut.FindAll("form[data-confirm*='Revogar a credencial Antiga']"));
        Assert.Contains("Revogada em", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Nunca", cut.Markup, StringComparison.Ordinal);

        Assert.NotNull(cut.Find("form[data-confirm*='Desativar Assistente']"));
        Assert.NotNull(cut.Find("form[data-confirm*='Remover o acesso de Assistente ao projeto Portal']"));
    }

    [Fact]
    public void AgentDetails_OffersOnlyContributorOrViewer_AndOnlyProjectsNotGrantedYet()
    {
        StubAgent();

        var cut = RenderDetails();

        cut.WaitForAssertion(() => Assert.Contains("Conceder acesso", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(["Colaborador", "Observador"], cut.FindAll("#grant-role option").Select(option => option.TextContent).ToList());
        Assert.Equal([OtherProject.ToString()], cut.FindAll("#grant-project option").Select(option => option.GetAttribute("value")).ToList());
        Assert.All(cut.FindAll("select[id^='role-'] option"), option => Assert.NotEqual("Gerente", option.TextContent));
        Assert.NotEmpty(cut.FindAll("select[id^='role-'] option"));
    }

    [Fact]
    public void AgentDetails_ShowsRecentOperations_WithTheRouteTemplateAndTheAuthorizationResult()
    {
        StubAgent();

        var cut = RenderDetails();

        cut.WaitForAssertion(() => Assert.Contains("/api/v1/agents/projects/{projectId:guid}/context", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Projeto sem acesso", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("project_not_visible", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("repetição", cut.Markup, StringComparison.Ordinal);
        Assert.Contains($"?pageSize=10", _context.Api.Calls.Single(call => call.Contains("/operations", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    /// <summary>
    /// D13, itens 1 a 6: o token aparece na resposta do próprio formulário, sem navegar (sem post-redirect-get), com o
    /// aviso de que não volta; o cliente não o registra em log; abrir a tela de novo não o mostra.
    /// </summary>
    [Fact]
    public void CreatingACredential_ShowsTheTokenOnce_InTheFormResponse_WithoutNavigatingOrLogging()
    {
        StubAgent();
        _context.Api.Json(HttpMethod.Post, $"{AgentsPath}/{AgentId}/credentials",
            new CreatedAgentCredential(Credential(Guid.NewGuid(), "Nova", AgentCredentialState.Active), FakeToken), HttpStatusCode.Created);
        var navigation = _context.Services.GetRequiredService<NavigationManager>();

        var cut = RenderDetails();
        cut.WaitForAssertion(() => Assert.Contains("Nova credencial", cut.Markup, StringComparison.Ordinal));
        var before = navigation.Uri;
        cut.Find("#credential-name").Change("Nova");
        cut.Find("#scope-reports").Change(true);
        cut.Find("#credential-name").Closest("form")!.Submit();

        cut.WaitForAssertion(() => Assert.Equal(FakeToken, cut.Find("#agent-token").GetAttribute("value")));
        Assert.Contains("Ele não aparece de novo", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("crie uma credencial nova e revogue esta", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("agent-token", cut.Find("[data-copy-target]").GetAttribute("data-copy-target"));
        Assert.Equal(before, navigation.Uri);
        Assert.DoesNotContain(FakeToken, navigation.Uri, StringComparison.Ordinal);
        Assert.DoesNotContain(_context.Logs.Messages, message => message.Contains("gsk_", StringComparison.Ordinal));

        // Os escopos marcados na tela, inclusive os dois que vêm marcados na primeira abertura.
        var body = _context.Api.Bodies[StubApiHandler.Key(HttpMethod.Post, $"{AgentsPath}/{AgentId}/credentials")];
        Assert.Contains("\"scopes\":[\"context:read\",\"tasks:read\",\"reports:write\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"expiresInDays\":90", body, StringComparison.Ordinal);

        using var reopened = new WebTestContext();
        StubAgent(reopened);
        var again = reopened.Render<AgentDetailsPage>(parameters => parameters
            .Add(p => p.OrganizationId, WebTestContext.OrganizationId)
            .Add(p => p.AgentId, AgentId));
        again.WaitForAssertion(() => Assert.Contains("gsk_ab12cd34…", again.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(FakeToken, again.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatingACredential_ThatTheApiRefuses_ShowsTheMessage_AndNoToken()
    {
        StubAgent();
        _context.Api.Problem(HttpMethod.Post, $"{AgentsPath}/{AgentId}/credentials", HttpStatusCode.Conflict, "O agente já tem 3 credenciais ativas.");

        var cut = RenderDetails();
        cut.WaitForAssertion(() => Assert.Contains("Nova credencial", cut.Markup, StringComparison.Ordinal));
        cut.Find("#credential-name").Change("Quarta");
        cut.Find("#credential-name").Closest("form")!.Submit();

        cut.WaitForAssertion(() => Assert.Contains("O agente já tem 3 credenciais ativas.", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#agent-token"));
    }

    [Fact]
    public void DisabledAgent_OffersReactivation_AndNoNewCredential()
    {
        StubAgent(status: AgentStatus.Disabled);

        var cut = RenderDetails();

        cut.WaitForAssertion(() => Assert.Contains("Reativar agente", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("Nova credencial", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Desativar agente", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentFromAnotherOrganization_ShowsNotFound()
    {
        _context.Membership(Permissions.AgentsManage);
        _context.Api.Problem(HttpMethod.Get, $"{AgentsPath}/{AgentId}", HttpStatusCode.NotFound, "Agente não encontrado.");

        var cut = RenderDetails();

        cut.WaitForAssertion(() => Assert.Contains("Não encontrado", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(_context.Api.Calls, call => call.Contains("/operations", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationsPage_SendsTheFilters_AndShowsTheResults()
    {
        StubAgent();
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"o/{WebTestContext.OrganizationId}/agentes/{AgentId}/operacoes?resultado=RateLimited&de=2026-11-01&pagina=2");

        var cut = _context.Render<AgentOperations>(parameters => parameters
            .Add(p => p.OrganizationId, WebTestContext.OrganizationId)
            .Add(p => p.AgentId, AgentId));

        cut.WaitForAssertion(() => Assert.Contains("Projeto sem acesso", cut.Markup, StringComparison.Ordinal));
        var call = _context.Api.Calls.Single(c => c.Contains("/operations", StringComparison.Ordinal));
        Assert.Contains("result=RateLimited", call, StringComparison.Ordinal);
        Assert.Contains("from=2026-11-01", call, StringComparison.Ordinal);
        Assert.Contains("page=2", call, StringComparison.Ordinal);
        Assert.Equal("RateLimited", cut.Find("#o-result option[selected]").GetAttribute("value"));
    }

    [Fact]
    public void NoStoreHeader_CoversEveryAgentPage_AndNothingElse()
    {
        Assert.True(ProjectManager.Web.Authentication.WebEndpoints.IsAgentPage($"/o/{WebTestContext.OrganizationId}/agentes"));
        Assert.True(ProjectManager.Web.Authentication.WebEndpoints.IsAgentPage($"/o/{WebTestContext.OrganizationId}/agentes/{AgentId}"));
        Assert.True(ProjectManager.Web.Authentication.WebEndpoints.IsAgentPage($"/o/{WebTestContext.OrganizationId}/agentes/{AgentId}/operacoes"));
        Assert.False(ProjectManager.Web.Authentication.WebEndpoints.IsAgentPage($"/o/{WebTestContext.OrganizationId}/projetos"));
        Assert.False(ProjectManager.Web.Authentication.WebEndpoints.IsAgentPage("/o/nao-e-guid/agentes"));
    }

    [Fact]
    public void CreatedCredential_NeverPrintsTheToken()
    {
        var created = new CreatedAgentCredential(Credential(ActiveCredential, "CI", AgentCredentialState.Active), FakeToken);

        Assert.DoesNotContain(FakeToken, created.ToString(), StringComparison.Ordinal);
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<AgentList> RenderList() =>
        _context.Render<AgentList>(parameters => parameters.Add(p => p.OrganizationId, WebTestContext.OrganizationId));

    private IRenderedComponent<AgentDetailsPage> RenderDetails() =>
        _context.Render<AgentDetailsPage>(parameters => parameters
            .Add(p => p.OrganizationId, WebTestContext.OrganizationId)
            .Add(p => p.AgentId, AgentId));

    private void StubAgent(AgentStatus status = AgentStatus.Active) => StubAgent(_context, status);

    private static void StubAgent(WebTestContext context, AgentStatus status = AgentStatus.Active)
    {
        context.Membership(Permissions.AgentsManage);
        var revoked = Credential(RevokedCredential, "Antiga", AgentCredentialState.Revoked) with
        {
            Prefix = "zz99yy88",
            RevokedAt = DateTimeOffset.UnixEpoch.AddDays(3),
        };
        context.Api.Json(HttpMethod.Get, $"{AgentsPath}/{AgentId}", new AgentDetails(
            AgentId, "Assistente", null, status, WebTestContext.UserId, DateTimeOffset.UnixEpoch,
            status == AgentStatus.Disabled ? DateTimeOffset.UnixEpoch : null,
            [Credential(ActiveCredential, "CI", AgentCredentialState.Active), revoked],
            [new AgentProjectAccessDetails(WebTestContext.ProjectId, "Portal", ProjectRole.Contributor, DateTimeOffset.UnixEpoch)]));
        context.Api.Json(HttpMethod.Get, $"{AgentsPath}/{AgentId}/operations", new Paged<AgentOperationItem>(
        [
            new AgentOperationItem(Guid.NewGuid(), ActiveCredential, "CI", DateTimeOffset.UnixEpoch, 12, "GET",
                "/api/v1/agents/projects/{projectId:guid}/context", null, null, 404,
                AgentAuthorizationResult.ProjectNotVisible, "project_not_visible", "trace-1", IdempotentReplay: false, ActivityOrigin.Cli),
            new AgentOperationItem(Guid.NewGuid(), ActiveCredential, "CI", DateTimeOffset.UnixEpoch, 30, "POST",
                "/api/v1/agents/tasks", WebTestContext.ProjectId, null, 201,
                AgentAuthorizationResult.Allowed, null, "trace-2", IdempotentReplay: true, ActivityOrigin.Cli),
        ], 1, 10, 2));
        context.Api.Json(HttpMethod.Get, $"organizations/{WebTestContext.OrganizationId}/projects", new Paged<ProjectSummary>(
        [
            new ProjectSummary(WebTestContext.ProjectId, "Portal", Priority.Medium, null, null, [], null, ProjectRole.Manager),
            new ProjectSummary(OtherProject, "Aplicativo", Priority.Medium, null, null, [], null, ProjectRole.Manager),
        ], 1, 100, 2));
    }

    private static AgentCredentialDetails Credential(Guid id, string name, AgentCredentialState state) =>
        new(id, name, "ab12cd34", ["context:read", "tasks:read"], state, DateTimeOffset.UnixEpoch.AddDays(90), DateTimeOffset.UnixEpoch, null, null);
}
