using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Agents;
using ProjectManager.Domain.Agents;

namespace ProjectManager.UnitTests.Domain;

/// <summary>Regras da idempotência de agentes (ADR-016, D9): formato da chave, validade e mesma requisição.</summary>
public sealed class IdempotencyRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('A', 64);

    [Theory]
    [InlineData("a", true)]
    [InlineData("018f2c1e-7b7a-7cc0-9d1e-3b5c0f6a4e21", true)]
    [InlineData("!~", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("com espaço", false)]
    [InlineData("ação", false)]
    [InlineData("tab\tkey", false)]
    [InlineData(null, false)]
    public void IsValidKey_AcceptsOnlyVisibleAscii(string? key, bool expected) =>
        Assert.Equal(expected, IdempotencyRecord.IsValidKey(key));

    [Fact]
    public void IsValidKey_LimitsTheLength()
    {
        Assert.True(IdempotencyRecord.IsValidKey(new string('k', IdempotencyRecord.KeyMaxLength)));
        Assert.False(IdempotencyRecord.IsValidKey(new string('k', IdempotencyRecord.KeyMaxLength + 1)));
    }

    [Fact]
    public void Record_ExpiresExactlyAfterItsLifetime()
    {
        var record = Create();

        Assert.False(record.IsExpiredAt(Now + IdempotencyRecord.Lifetime - TimeSpan.FromTicks(1)));
        Assert.True(record.IsExpiredAt(Now + IdempotencyRecord.Lifetime));
    }

    [Fact]
    public void Record_RefusesInvalidKeysHashesAndOversizedResponses()
    {
        Assert.Throws<ArgumentException>(() => Create(key: "com espaço"));
        Assert.Throws<ArgumentException>(() => Create(hash: "abc"));
        Assert.Throws<ArgumentException>(() => Create(body: new string('x', IdempotencyRecord.ResponseBodyMaxLength + 1)));
        Assert.Equal(IdempotencyRecord.ResponseBodyMaxLength, Create(body: new string('x', IdempotencyRecord.ResponseBodyMaxLength)).ResponseBody.Length);
    }

    [Fact]
    public void StoredResponse_MatchesOnlyTheSameMethodRouteAndHash()
    {
        var agent = new ActiveAgent(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new HashSet<string>(StringComparer.Ordinal));
        var stored = new StoredIdempotentResponse("POST", "api/v1/agents/tasks", Hash, 201, "{}", null, null);
        IdempotentRequest Request(string method = "POST", string route = "api/v1/agents/tasks", string hash = "") =>
            new(agent, "chave", method, route, hash.Length == 0 ? Hash : hash);

        Assert.True(stored.Matches(Request()));
        Assert.False(stored.Matches(Request(method: "PUT")));
        Assert.False(stored.Matches(Request(route: "api/v1/agents/activities")));
        Assert.False(stored.Matches(Request(hash: new string('B', 64))));
        Assert.False(stored.Matches(Request(route: "API/V1/AGENTS/TASKS")));
    }

    private static IdempotencyRecord Create(string key = "chave-1", string? hash = null, string body = "{}") =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), key, "POST", "api/v1/agents/tasks",
            hash ?? Hash, 201, body, "/api/v1/agents/tasks/1", Guid.CreateVersion7(), Now);
}
