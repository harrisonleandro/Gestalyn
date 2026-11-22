using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Projects;
using ProjectManager.Application.Tasks;
using ProjectManager.Application.Workflows;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Projects;
using ProjectManager.Infrastructure.MultiTenancy;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Etapa 5 (ADR-016, D4 e D5) no serviço real de tarefas, com um workflow personalizado que tem um atalho
/// "Em andamento → Concluída" sem aprovação e um estado Cancelada. Com a concessão calculada por
/// <see cref="AgentAccessGrant"/>, o agente não recebe aprovação nem Concluída entre as transições disponíveis; as
/// pessoas continuam vendo exatamente as mesmas de antes. A execução de transições por agentes chega com a rota da
/// etapa 8, depois do ator nos serviços (etapa 6).
/// </summary>
public sealed class AgentTransitionServiceTests(PostgresDatabaseFixture database, MailpitFixture mailpit) : IDisposable
{
    private readonly ApiFactory _factory = new(database.ConnectionString, mailpit);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CustomWorkflow_OffersAgentsNeitherApprovalNorDone_AndPeopleSeeTheSameAsBefore()
    {
        var kit = new TaskTestKit(_factory.CreateClient(), mailpit);
        var workspace = await kit.CreateWorkspaceAsync("Workflow personalizado");
        var contributor = await kit.AddTeamMemberAsync(workspace, "Contributor");
        var workflow = await ReadWorkflowAsync(kit, workspace);
        Guid State(string name) => workflow.States.Single(s => s.Name == name).Id;

        var canceled = await kit.CreateInProjectAsync<WorkflowStateDetails>(workspace, "/workflow/states", new { name = "Cancelada", category = "Canceled" });
        await AddTransitionAsync(kit, workspace, State("Em andamento"), State("Concluída"), requiresApproval: false);
        await AddTransitionAsync(kit, workspace, State("Em andamento"), canceled.Id, requiresApproval: false);

        var task = await kit.CreateTaskAsync(workspace, "Tarefa com atalho");
        foreach (var target in new[] { "A fazer", "Em andamento" })
        {
            using var move = await kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/transitions", workspace.Owner, new { toStateId = State(target) });
            Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        }

        // Pessoas: o atalho sem aprovação aparece para o Colaborador, como antes desta etapa.
        using var asContributor = await kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, string.Empty, contributor);
        var forContributor = (await TaskTestKit.ReadAsync<TaskDetails>(asContributor)).AvailableTransitions.Select(t => t.ToStateName).ToHashSet();
        Assert.Equal(["A fazer", "Bloqueada", "Cancelada", "Concluída", "Em revisão"], forContributor.Order(StringComparer.Ordinal));

        // Agente Colaborador com todos os escopos: as mesmas, menos Concluída.
        var forAgent = await AvailableForAgentAsync(workspace, task.Id, AgentScopes.All);
        Assert.Equal(["A fazer", "Bloqueada", "Cancelada", "Em revisão"], forAgent.Order(StringComparer.Ordinal));

        // Sem tasks:write, nenhuma transição é oferecida.
        Assert.Empty(await AvailableForAgentAsync(workspace, task.Id, [AgentScopes.TasksRead]));

        // Na revisão, a única saída para Concluída exige aprovação: oferecida ao Gerente, nunca ao agente.
        using (var review = await kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/transitions", workspace.Owner, new { toStateId = State("Em revisão") }))
        {
            var forOwner = (await TaskTestKit.ReadAsync<TaskDetails>(review)).AvailableTransitions;
            Assert.Contains(forOwner, t => t.ToStateName == "Concluída" && t.RequiresApproval);
        }

        Assert.DoesNotContain("Concluída", await AvailableForAgentAsync(workspace, task.Id, AgentScopes.All));
    }

    public void Dispose() => _factory.Dispose();

    private async Task<IReadOnlyList<string>> AvailableForAgentAsync(Workspace workspace, Guid taskId, IEnumerable<string> scopes)
    {
        using var scope = _factory.Services.CreateScope();
        var agent = new ActiveAgent(workspace.OrganizationId, Guid.CreateVersion7(), Guid.CreateVersion7(), scopes.ToHashSet(StringComparer.Ordinal));
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetActiveAgent(agent);
        var grant = AgentAccessGrant.Compute(agent, agentIsActive: true, workspace.ProjectId, ProjectRole.Contributor, AgentRequirement.TasksRead).Grant!;

        var details = await scope.ServiceProvider.GetRequiredService<ITaskService>().GetAsync(grant, taskId, CancellationToken);
        return details.Value!.AvailableTransitions.Select(t => t.ToStateName).ToList();
    }

    private static async Task<WorkflowDetails> ReadWorkflowAsync(TaskTestKit kit, Workspace workspace)
    {
        using var response = await kit.SendToProjectAsync(HttpMethod.Get, workspace, "/workflow", workspace.Owner);
        return await TaskTestKit.ReadAsync<WorkflowDetails>(response);
    }

    private static async Task AddTransitionAsync(TaskTestKit kit, Workspace workspace, Guid from, Guid to, bool requiresApproval)
    {
        using var response = await kit.Projects.SendAsync(
            HttpMethod.Post,
            "/api/v1/workflow/transitions",
            workspace.Owner,
            new { fromStateId = from, toStateId = to, requiresApproval },
            workspace.OrganizationId.ToString());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
