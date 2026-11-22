using System.Net;
using Npgsql;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Common;
using ProjectManager.Application.Tasks;
using ProjectManager.Application.Workflows;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Workflows;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Ponta a ponta da Fase 10 (estratégia de testes), com um token real emitido pelo sistema: uma pessoa provisiona o
/// agente; o agente lê o contexto, cria e altera uma tarefa, leva-a à revisão, registra atividade e relatório; as
/// tentativas de aprovar e de concluir são recusadas e auditadas; uma pessoa aprova; a credencial é revogada. No fim,
/// o token não aparece em nenhuma tabela nem no log.
/// </summary>
public sealed class AgentEndToEndTests(PostgresDatabaseFixture database, MailpitFixture mailpit) : IDisposable
{
    private readonly ApiFactory _factory = new(database.ConnectionString, mailpit);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Agent_WorksOnATask_FromProvisioningToRevocation()
    {
        var tasks = new TaskTestKit(_factory.CreateClient(), mailpit);
        var agents = new AgentTestKit(_factory.CreateClient(), mailpit);
        var workspace = await tasks.CreateWorkspaceAsync("Projeto ponta a ponta");
        var manage = $"/api/v1/organizations/{workspace.OrganizationId}/agents";

        // 1. Provisionamento por uma pessoa com agents.manage: o token vem só na resposta da criação, sem cache.
        var agent = await agents.CreateAgentAsync(workspace.Owner, workspace.OrganizationId, "Agente ponta a ponta");
        string token;
        Guid credentialId;
        using (var created = await agents.ManageAsync(HttpMethod.Post, workspace.Owner, workspace.OrganizationId, $"/{agent.Id}/credentials",
            new { name = "Ponta a ponta", scopes = AgentScopes.All, expiresInDays = 30 }))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Contains("no-store", created.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
            var body = await TaskTestKit.ReadAsync<CreatedAgentCredential>(created);
            (token, credentialId) = (body.Token, body.Credential.Id);
        }

        using (var grant = await agents.ManageAsync(HttpMethod.Put, workspace.Owner, workspace.OrganizationId, $"/{agent.Id}/projects/{workspace.ProjectId}", new { role = "Contributor" }))
        {
            Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        }

        using (var list = await agents.ManageAsync(HttpMethod.Get, workspace.Owner, workspace.OrganizationId, $"/{agent.Id}/credentials"))
        {
            Assert.DoesNotContain(token, await list.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
        }

        // 2. O agente se identifica e lê o contexto do projeto.
        using (var me = await agents.SendAsAgentAsync(token, HttpMethod.Get, "/api/v1/agents/me"))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        using (var context = await agents.SendAsAgentAsync(token, HttpMethod.Get, $"/api/v1/agents/projects/{workspace.ProjectId}/context"))
        {
            Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        }

        // 3. Cria a tarefa com Idempotency-Key (o replay não duplica) e a altera pela versão.
        var key = new Dictionary<string, string> { [IdempotencyRecord.HeaderName] = Guid.NewGuid().ToString() };
        var create = new { projectId = workspace.ProjectId, title = "Implementar a tela de login" };
        TaskDetails task;
        using (var first = await agents.SendAsAgentAsync(token, HttpMethod.Post, "/api/v1/agents/tasks", create, key))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            task = await TaskTestKit.ReadAsync<TaskDetails>(first);
        }

        using (var replay = await agents.SendAsAgentAsync(token, HttpMethod.Post, "/api/v1/agents/tasks", create, key))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.True(replay.Headers.Contains(IdempotencyRecord.ReplayedHeaderName));
            Assert.Equal(task.Id, (await TaskTestKit.ReadAsync<TaskDetails>(replay)).Id);
        }

        using (var projectTasks = await tasks.SendToProjectAsync(HttpMethod.Get, workspace, "/tasks", workspace.Owner))
        {
            Assert.Single((await TaskTestKit.ReadAsync<PagedResult<TaskSummary>>(projectTasks)).Items);
        }

        Assert.Equal((agent.Id, ActorKind.Agent), (task.CreatedBy.Id, task.CreatedBy.Kind));
        var version = await PatchAsync(agents, token, task.Id, new { description = "Formulário e validação", expectedVersion = task.Version });

        // 4. Leva a tarefa até a revisão. A pessoa abre um atalho sem aprovação para Concluída.
        var workflow = await ReadWorkflowAsync(tasks, workspace);
        Guid State(string name) => workflow.States.Single(s => s.Name == name).Id;
        using (var shortcut = await tasks.Projects.SendAsync(HttpMethod.Post, "/api/v1/workflow/transitions", workspace.Owner,
            new { fromStateId = State("Em andamento"), toStateId = State("Concluída"), requiresApproval = false }, workspace.OrganizationId.ToString()))
        {
            Assert.Equal(HttpStatusCode.Created, shortcut.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, await TransitionAsync(agents, token, task.Id, State("A fazer"), version++));
        Assert.Equal(HttpStatusCode.OK, await TransitionAsync(agents, token, task.Id, State("Em andamento"), version++));

        // 6a. Entrar em Concluída pelo atalho sem aprovação: recusado.
        Assert.Equal(HttpStatusCode.Forbidden, await TransitionAsync(agents, token, task.Id, State("Concluída"), version));
        Assert.Equal(HttpStatusCode.OK, await TransitionAsync(agents, token, task.Id, State("Em revisão"), version++));

        // 5. Atividade e relatório: os níveis vêm separados e o estado da tarefa não muda.
        using (var activity = await agents.SendAsAgentAsync(token, HttpMethod.Post, "/api/v1/agents/activities",
            new { projectId = workspace.ProjectId, kind = "Note", summary = "Pronto para revisão" }, NewKey()))
        {
            Assert.Equal(HttpStatusCode.Created, activity.StatusCode);
        }

        WorkReportReceipt receipt;
        using (var report = await agents.SendAsAgentAsync(token, HttpMethod.Post, "/api/v1/agents/reports", new
        {
            projectId = workspace.ProjectId,
            taskId = task.Id,
            summary = "Tela de login implementada",
            tests = new { command = "dotnet test", passed = 12, failed = 0 },
            suggestedStatus = State("Concluída"),
        }, NewKey()))
        {
            Assert.Equal(HttpStatusCode.Created, report.StatusCode);
            receipt = await TaskTestKit.ReadAsync<WorkReportReceipt>(report);
        }

        Assert.Equal(
            (true, true, WorkReportLevels.NotVerified, WorkReportLevels.NotVerified, WorkReportLevels.Pending),
            (receipt.Evidence.Declared, receipt.Evidence.Accepted, receipt.Evidence.TestsExecuted, receipt.Evidence.CiConfirmed, receipt.Evidence.HumanApproval));
        Assert.Equal("Em revisão", receipt.TaskStatus!.Name);
        Assert.Equal(true, receipt.ApprovalRequired);

        // 6b. A saída da revisão exige aprovação: recusada, sem mudar o estado.
        Assert.Equal(HttpStatusCode.Forbidden, await TransitionAsync(agents, token, task.Id, State("Concluída"), version));
        var reviewed = await AgentTaskAsync(agents, token, task.Id);
        Assert.Equal(("Em revisão", version), (reviewed.State.Name, reviewed.Version));

        // 7. Uma pessoa aprova; o relatório passa a mostrar a aprovação humana.
        using (var approve = await tasks.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/transitions", workspace.Owner,
            new { toStateId = State("Concluída"), expectedStateId = State("Em revisão") }))
        {
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        }

        using (var details = await tasks.Projects.SendAsync(HttpMethod.Get, $"/api/v1/reports/{receipt.ReportId}", workspace.Owner, null, workspace.OrganizationId.ToString()))
        {
            var read = await TaskTestKit.ReadAsync<WorkReportDetails>(details);
            Assert.Equal(WorkReportLevels.Approved, read.Evidence.HumanApproval);
            Assert.Equal(workspace.Owner.Id, read.Approval!.ApprovedById);
        }

        // 8. Revogação: a chamada seguinte recebe 401 e também fica registrada.
        using (var revoke = await agents.ManageAsync(HttpMethod.Post, workspace.Owner, workspace.OrganizationId, $"/{agent.Id}/credentials/{credentialId}/revoke"))
        {
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        }

        using (var after = await agents.SendAsAgentAsync(token, HttpMethod.Get, "/api/v1/agents/me"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        }

        // Auditoria: as duas recusas por regra e a revogação estão nas operações; as escritas, nas atividades.
        PagedResult<AgentOperationItem> operations;
        using (var response = await agents.ManageAsync(HttpMethod.Get, workspace.Owner, workspace.OrganizationId, $"/{agent.Id}/operations?pageSize=100"))
        {
            operations = await TaskTestKit.ReadAsync<PagedResult<AgentOperationItem>>(response);
        }

        Assert.Equal(
            ["agent_cannot_enter_done", "agent_cannot_approve"],
            operations.Items.Where(o => o.AuthorizationResult == AgentAuthorizationResult.BusinessRuleDenied).Reverse().Select(o => o.DenialCode));
        Assert.Single(operations.Items, o => o.AuthorizationResult == AgentAuthorizationResult.CredentialRevoked && o.StatusCode == 401);
        Assert.Single(operations.Items, o => o.IdempotentReplay);
        Assert.All(operations.Items, o => Assert.False(string.IsNullOrEmpty(o.CorrelationId)));

        using (var activities = await tasks.SendToProjectAsync(HttpMethod.Get, workspace, "/activities?pageSize=100", workspace.Owner))
        {
            var items = (await TaskTestKit.ReadAsync<PagedResult<ActivityItem>>(activities)).Items;
            Assert.Contains(items, i => i.Action == ActivityActions.TaskCreated && i.ActorKind == ActorKind.Agent && i.ActorId == agent.Id);
            Assert.Contains(items, i => i.Action == ActivityActions.WorkReportReceived && i.ActorKind == ActorKind.Agent);
            Assert.Contains(items, i => i.Action == ActivityActions.TaskTransitioned && i.ActorKind == ActorKind.User);
        }

        // O token não está em nenhuma tabela (só o hash e o prefixo) nem no log.
        Assert.Empty(await TablesContainingAsync(token));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        Assert.Equal(["agent_credentials"], await TablesContainingAsync(hash));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Contains(token, StringComparison.Ordinal));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Contains(token[..12], StringComparison.Ordinal));
    }

    public void Dispose() => _factory.Dispose();

    private static Dictionary<string, string> NewKey() => new() { [IdempotencyRecord.HeaderName] = Guid.NewGuid().ToString() };

    private static async Task<int> PatchAsync(AgentTestKit agents, string token, Guid taskId, object body)
    {
        using var response = await agents.SendAsAgentAsync(token, HttpMethod.Patch, $"/api/v1/agents/tasks/{taskId}", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await TaskTestKit.ReadAsync<TaskDetails>(response)).Version;
    }

    private static async Task<HttpStatusCode> TransitionAsync(AgentTestKit agents, string token, Guid taskId, Guid toStateId, int expectedVersion)
    {
        using var response = await agents.SendAsAgentAsync(token, HttpMethod.Post, $"/api/v1/agents/tasks/{taskId}/transitions", new { toStateId, expectedVersion }, NewKey());
        return response.StatusCode;
    }

    private static async Task<TaskDetails> AgentTaskAsync(AgentTestKit agents, string token, Guid taskId)
    {
        using var response = await agents.SendAsAgentAsync(token, HttpMethod.Get, $"/api/v1/agents/tasks/{taskId}");
        return await TaskTestKit.ReadAsync<TaskDetails>(response);
    }

    private static async Task<WorkflowDetails> ReadWorkflowAsync(TaskTestKit tasks, Workspace workspace)
    {
        using var response = await tasks.SendToProjectAsync(HttpMethod.Get, workspace, "/workflow", workspace.Owner);
        return await TaskTestKit.ReadAsync<WorkflowDetails>(response);
    }

    /// <summary>Procura o texto em todas as linhas de todas as tabelas do schema público, como texto.</summary>
    private async Task<List<string>> TablesContainingAsync(string value)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname = 'public'", connection))
        await using (var reader = await list.ExecuteReaderAsync(CancellationToken))
        {
            while (await reader.ReadAsync(CancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var found = new List<string>();
        foreach (var table in tables)
        {
            await using var search = new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM \"{table}\" t WHERE strpos(t::text, @value) > 0)", connection);
            search.Parameters.AddWithValue("value", value);
            if ((bool)(await search.ExecuteScalarAsync(CancellationToken))!)
            {
                found.Add(table);
            }
        }

        return found;
    }
}
