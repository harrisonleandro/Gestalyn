using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManager.Domain.Agents;
using ProjectManager.Infrastructure.Agents;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Retenção (ADR-016, D12, etapa 10): o ciclo apaga só o vencido (operações com mais de 180 dias, registros de
/// idempotência com mais de 7 e contadores com mais de 1), atravessando organizações; o gatilho recusa o
/// <c>DELETE</c> direto de operação recente; um ciclo repetido não apaga nada; com a trava de outra instância, o ciclo
/// é pulado; relatórios de trabalho ficam intactos.
/// </summary>
[Collection(RetentionTestGroup.Name)]
public sealed class AgentRetentionTests(PostgresDatabaseFixture database, MailpitFixture mailpit) : IDisposable
{
    private const long RetentionLockKey = 0x6773742D72746E;

    private readonly ApiFactory _factory = new(database.ConnectionString, mailpit);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cycle_DeletesOnlyExpiredRecords_InEveryOrganization_AndIsIdempotent()
    {
        var first = await SeedAsync("Primeira organização");
        var second = await SeedAsync("Segunda organização");

        var result = await RunCycleAsync();

        Assert.False(result.Skipped);
        Assert.True(result.Operations >= 4, $"operações apagadas: {result.Operations}");
        foreach (var seed in new[] { first, second })
        {
            Assert.Equal(["recente"], await LabelsAsync("SELECT denial_code FROM agent_operations WHERE credential_id = @id", seed.CredentialId));
            Assert.Equal(["recente"], await LabelsAsync("SELECT key FROM idempotency_records WHERE credential_id = @id", seed.CredentialId));
            Assert.Single(await LabelsAsync("SELECT request_count::text FROM agent_rate_limit_counters WHERE credential_id = @id", seed.CredentialId));
            Assert.Single(await LabelsAsync("SELECT summary FROM work_reports WHERE credential_id = @id", seed.CredentialId));
        }

        var again = await RunCycleAsync();
        Assert.Equal((0, 0, 0), (again.Operations, again.IdempotencyRecords, again.RateLimitCounters));
        Assert.Equal(["recente"], await LabelsAsync("SELECT denial_code FROM agent_operations WHERE credential_id = @id", first.CredentialId));
    }

    [Fact]
    public async Task Database_RefusesToDeleteOrChangeRecentOperations()
    {
        var seed = await SeedAsync("Organização protegida");

        Assert.Equal("23000", await ExecuteAsync("DELETE FROM agent_operations WHERE credential_id = @id AND denial_code = 'recente'", seed.CredentialId));
        Assert.Equal("23000", await ExecuteAsync("UPDATE agent_operations SET status_code = 200 WHERE credential_id = @id", seed.CredentialId));
        Assert.Equal("23000", await ExecuteAsync("TRUNCATE agent_operations", seed.CredentialId));

        // A exclusão vencida é aceita pelo gatilho; numa transação desfeita, nada some de fato.
        Assert.Null(await ExecuteAsync("DELETE FROM agent_operations WHERE credential_id = @id AND denial_code = 'vencida'", seed.CredentialId));
    }

    [Fact]
    public async Task Cycle_IsSkipped_WhileAnotherInstanceHoldsTheLock()
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using (var lockCommand = new NpgsqlCommand($"SELECT pg_advisory_lock({RetentionLockKey})", connection))
        {
            await lockCommand.ExecuteNonQueryAsync(CancellationToken);
        }

        try
        {
            Assert.True((await RunCycleAsync()).Skipped);
        }
        finally
        {
            await using var unlock = new NpgsqlCommand($"SELECT pg_advisory_unlock({RetentionLockKey})", connection);
            await unlock.ExecuteNonQueryAsync(CancellationToken);
        }

        Assert.False((await RunCycleAsync()).Skipped);
    }

    [Fact]
    public void Api_RefusesToStart_WithARetentionShorterThanTheTrigger()
    {
        using var factory = new ApiFactory(database.ConnectionString, mailpit, new Dictionary<string, string?>
        {
            ["Agents:Retention:OperationRetentionDays"] = (AgentOperation.MinRetentionDays - 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

        var exception = Assert.ThrowsAny<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("180 dias", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _factory.Dispose();

    private async Task<AgentRetentionResult> RunCycleAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentRetentionCleaner>().RunAsync(CancellationToken);
    }

    /// <summary>
    /// Uma organização com agente e credencial reais, e registros vencidos e recentes inseridos direto no banco
    /// (a inserção é permitida; só a alteração e a exclusão são protegidas), além de um relatório de trabalho.
    /// </summary>
    private async Task<Seed> SeedAsync(string name)
    {
        var tasks = new TaskTestKit(_factory.CreateClient(), mailpit);
        var agents = new AgentTestKit(_factory.CreateClient(), mailpit);
        var workspace = await tasks.CreateWorkspaceAsync(name);
        var agentId = (await agents.CreateAgentAsync(workspace.Owner, workspace.OrganizationId, "Agente da limpeza")).Id;
        var credential = (await agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, [.. AgentScopes.All])).Credential.Id;

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        foreach (var (label, age) in new[] { ("vencida", "200 days"), ("vencida", "181 days"), ("recente", "10 days") })
        {
            await InsertAsync(connection, $$"""
                INSERT INTO agent_operations (id, organization_id, agent_id, credential_id, occurred_at, duration_ms, http_method,
                    route_template, status_code, authorization_result, denial_code, idempotent_replay, client_origin)
                VALUES (gen_random_uuid(), @organization, @agent, @credential, now() - interval '{{age}}', 1, 'GET',
                    '/api/v1/agents/me', 200, 'Allowed', '{{label}}', false, 'Api')
                """, workspace.OrganizationId, agentId, credential);
        }

        foreach (var (label, age) in new[] { ("vencida", "8 days"), ("recente", "2 days") })
        {
            await InsertAsync(connection, $$"""
                INSERT INTO idempotency_records (id, organization_id, agent_id, credential_id, key, method, route_template,
                    request_hash, response_status, response_body, created_at, expires_at)
                VALUES (gen_random_uuid(), @organization, @agent, @credential, '{{label}}', 'POST', 'api/v1/agents/tasks',
                    repeat('A', 64), 201, '{}', now() - interval '{{age}}', now() - interval '{{age}}' + interval '1 day')
                """, workspace.OrganizationId, agentId, credential);
        }

        foreach (var age in new[] { "3 days", "1 minute" })
        {
            await InsertAsync(connection, $$"""
                INSERT INTO agent_rate_limit_counters (credential_id, organization_id, window_start, request_count)
                VALUES (@credential, @organization, date_trunc('minute', now() - interval '{{age}}'), 1)
                """, workspace.OrganizationId, agentId, credential);
        }

        await InsertAsync(connection, """
            INSERT INTO work_reports (id, organization_id, project_id, agent_id, credential_id, summary, files, received_at)
            VALUES (gen_random_uuid(), @organization, @project, @agent, @credential, 'Relatório antigo', '{}', now() - interval '400 days')
            """, workspace.OrganizationId, agentId, credential, workspace.ProjectId);

        return new Seed(workspace.OrganizationId, credential);
    }

    private static async Task InsertAsync(NpgsqlConnection connection, string sql, Guid organization, Guid agent, Guid credential, Guid? project = null)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("organization", organization);
        command.Parameters.AddWithValue("agent", agent);
        command.Parameters.AddWithValue("credential", credential);
        if (project is { } projectId)
        {
            command.Parameters.AddWithValue("project", projectId);
        }

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<List<string>> LabelsAsync(string sql, Guid credentialId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", credentialId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var labels = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            labels.Add(reader.GetString(0));
        }

        return labels;
    }

    /// <summary>Executa numa transação sempre desfeita: nada some do banco compartilhado, com ou sem o gatilho.</summary>
    private async Task<string?> ExecuteAsync(string sql, Guid credentialId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", credentialId);
        try
        {
            await command.ExecuteNonQueryAsync(CancellationToken);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken);
        }
    }

    private sealed record Seed(Guid OrganizationId, Guid CredentialId);
}

/// <summary>
/// Os testes de retenção apagam registros vencidos de todas as organizações e disputam a trava da limpeza: rodam em
/// sequência entre si.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RetentionTestGroup
{
    public const string Name = "Retenção de agentes";
}
