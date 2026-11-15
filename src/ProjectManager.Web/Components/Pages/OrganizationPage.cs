using Microsoft.AspNetCore.Components;
using ProjectManager.Web.Api;


namespace ProjectManager.Web.Components.Pages;

/// <summary>
/// Base das páginas de uma organização (<c>/o/{organizationId}/...</c>, ADR-015, D3). Carrega o papel e as permissões
/// do usuário (<c>GET /organizations/{id}/me</c>); não membro recebe "não encontrado", como na API. As ações são
/// formulários SSR: depois de uma alteração bem-sucedida, a página redireciona para si mesma com um aviso.
/// </summary>
public abstract class OrganizationPage : ComponentBase
{
    [Parameter]
    public Guid OrganizationId { get; set; }

    [Inject]
    protected OrganizationsApi Organizations { get; set; } = default!;

    [Inject]
    protected MembershipCache Memberships { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    [Inject]
    protected Display Display { get; set; } = default!;

    protected MyMembership? Membership { get; private set; }

    /// <summary>Falha ao carregar a página (403, 404, API indisponível): a página mostra o erro no lugar do conteúdo.</summary>
    protected ApiResult? LoadError { get; private set; }

    /// <summary>Resultado da última ação do formulário, quando ela falhou.</summary>
    protected ApiResult? ActionResult { get; set; }

    protected string OrgPath => $"/o/{OrganizationId}";

    protected override async Task OnInitializedAsync()
    {
        var membership = await Memberships.GetAsync(OrganizationId);
        if (!membership.Result.Succeeded || membership.Value is null)
        {
            LoadError = membership.Result;
            return;
        }

        Membership = membership.Value;
        await LoadAsync();
    }

    /// <summary>Carrega os dados da página depois de confirmada a associação.</summary>
    protected virtual Task LoadAsync() => Task.CompletedTask;

    protected void Fail(ApiResult result) => LoadError = result;

    /// <summary>Lê um resultado de carga; em falha, registra o erro da página e devolve <c>null</c>.</summary>
    protected T? Take<T>(ApiResult<T> response)
        where T : class
    {
        if (response.Result.Succeeded && response.Value is not null)
        {
            return response.Value;
        }

        LoadError ??= response.Result;
        return null;
    }

    protected void Complete<T>(ApiResult<T> response, string flash, string? target = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        Complete(response.Result, flash, target);
    }

    /// <summary>Depois de uma ação: em sucesso, recarrega a página (ou vai para <paramref name="target"/>) com o aviso.</summary>
    protected void Complete(ApiResult result, string flash, string? target = null)
    {
        if (!result.Succeeded)
        {
            ActionResult = result;
            return;
        }

        Navigation.NavigateTo(Flash.With(target ?? Navigation.Uri, flash));
    }
}

/// <summary>
/// Base das páginas de um projeto. O projeto vem da rota (<c>{projectId}</c>) ou do recurso aberto (tarefa, documento,
/// decisão), e traz as permissões efetivas do usuário nele.
/// </summary>
public abstract class ProjectPage : OrganizationPage
{
    [Parameter]
    public Guid ProjectId { get; set; }

    [Inject]
    protected ProjectsApi Projects { get; set; } = default!;

    protected ProjectDetails? Project { get; private set; }

    protected string ProjectPath => $"{OrgPath}/projetos/{Project?.Id ?? ProjectId}";

    protected bool Can(string permission) => Project?.Can(permission) == true;

    protected sealed override async Task LoadAsync()
    {
        var projectId = await ResolveProjectIdAsync();
        if (projectId is null)
        {
            return;
        }

        Project = Take(await Projects.GetAsync(OrganizationId, projectId.Value));
        if (Project is not null)
        {
            await LoadProjectAsync();
        }
    }

    /// <summary>Projeto da página. As páginas de um recurso carregam o recurso aqui e devolvem o projeto dele.</summary>
    protected virtual Task<Guid?> ResolveProjectIdAsync() => Task.FromResult<Guid?>(ProjectId);

    protected virtual Task LoadProjectAsync() => Task.CompletedTask;
}
