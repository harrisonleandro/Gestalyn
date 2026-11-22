using ProjectManager.Application.Agents;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Agents;
using ProjectManager.Infrastructure.Agents;

namespace ProjectManager.UnitTests.Application;

/// <summary>Regras puras da auditoria, do rate limiting e da retenção de agentes (ADR-016, D10 a D12).</summary>
public sealed class AgentOperationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(120, false, false)]
    [InlineData(121, true, true)]
    [InlineData(122, true, false)]
    public void RateLimitHit_LimitsAboveThePermit_AndMarksOnlyTheFirstRejection(int count, bool limited, bool first)
    {
        var hit = new AgentRateLimitHit(count, 120, Now.AddSeconds(30), Now);

        Assert.Equal((limited, first), (hit.IsLimited, hit.IsFirstRejection));
    }

    [Fact]
    public void RetryAfter_RoundsUpToTheEndOfTheWindow_AndIsNeverZero()
    {
        Assert.Equal(30, new AgentRateLimitHit(121, 120, Now.AddSeconds(29.2), Now).RetryAfterSeconds);
        Assert.Equal(1, new AgentRateLimitHit(121, 120, Now, Now).RetryAfterSeconds);
    }

    [Fact]
    public void AuditHealth_IsDegraded_WhileTheLastWriteFailed()
    {
        var health = new AgentAuditHealth();
        Assert.False(health.IsDegraded);

        health.Failed(Now);
        Assert.True(health.IsDegraded);

        health.Succeeded(Now.AddSeconds(1));
        Assert.False(health.IsDegraded);

        health.Failed(Now.AddSeconds(2));
        Assert.True(health.IsDegraded);
    }

    [Fact]
    public void RetentionHealth_IsDegraded_AfterThreeIntervalsWithoutACycle()
    {
        var clock = new FixedClock(Now);
        var options = new AgentRetentionOptions { Interval = TimeSpan.FromHours(1), InitialDelay = TimeSpan.FromMinutes(1) };
        var health = new AgentRetentionHealth(clock);

        Assert.False(health.IsDegraded(options, Now.AddHours(3)));
        Assert.True(health.IsDegraded(options, Now.AddHours(3).AddMinutes(2)));

        health.CycleCompleted(Now.AddHours(4));
        Assert.False(health.IsDegraded(options, Now.AddHours(7)));
        Assert.True(health.IsDegraded(options, Now.AddHours(7).AddSeconds(1)));
    }

    [Theory]
    [InlineData(AgentOperation.MinRetentionDays, true)]
    [InlineData(365, true)]
    [InlineData(AgentOperation.MinRetentionDays - 1, false)]
    public void RetentionOptions_NeverGoBelowTheTrigger(int days, bool valid) =>
        Assert.Equal(valid, new AgentRetentionOptions { OperationRetentionDays = days }.IsValid());

    [Fact]
    public void RetentionOptions_RequireAPositiveIntervalAndBatch()
    {
        Assert.True(new AgentRetentionOptions().IsValid());
        Assert.False(new AgentRetentionOptions { Interval = TimeSpan.Zero }.IsValid());
        Assert.False(new AgentRetentionOptions { BatchSize = 0 }.IsValid());
        Assert.False(new AgentRetentionOptions { IdempotencyRetentionDays = 0 }.IsValid());
    }

    [Theory]
    [InlineData(120, 60, true)]
    [InlineData(0, 60, false)]
    [InlineData(120, 0, false)]
    [InlineData(120, 3601, false)]
    public void RateLimitOptions_AreValidated(int permit, int window, bool valid) =>
        Assert.Equal(valid, new AgentRateLimitOptions { PermitLimit = permit, WindowSeconds = window }.IsValid());

    [Fact]
    public void Operation_TruncatesFreeTextToTheColumns_AndNeverHasNegativeDuration()
    {
        var operation = new AgentOperation(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Now, -5, "GET",
            "/" + new string('r', AgentOperation.RouteTemplateMaxLength + 50), null, null, 200,
            AgentAuthorizationResult.Allowed, new string('c', AgentOperation.DenialCodeMaxLength + 1),
            new string('t', AgentOperation.CorrelationIdMaxLength + 1), idempotentReplay: false, ActivityOrigin.Api);

        Assert.Equal(0, operation.DurationMs);
        Assert.Equal(AgentOperation.RouteTemplateMaxLength, operation.RouteTemplate.Length);
        Assert.Equal(AgentOperation.DenialCodeMaxLength, operation.DenialCode!.Length);
        Assert.Equal(AgentOperation.CorrelationIdMaxLength, operation.CorrelationId!.Length);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
