using System.Text.Json;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Workflows;
using ProjectManager.Infrastructure.Activity;

namespace ProjectManager.UnitTests.Domain;

/// <summary>Regras dos registros de atividade (ADR-014): origem, autor, resumo, alterações e registros manuais.</summary>
public sealed class ActivityRulesTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly ActivityActor Person = new(Guid.CreateVersion7(), ActorKind.User, ActivityOrigin.Web, "0HN-REQ");

    [Theory]
    [InlineData("web", ActivityOrigin.Web)]
    [InlineData(" WEB ", ActivityOrigin.Web)]
    [InlineData("cli", ActivityOrigin.Cli)]
    [InlineData("Mcp", ActivityOrigin.Mcp)]
    [InlineData(null, ActivityOrigin.Api)]
    [InlineData("", ActivityOrigin.Api)]
    [InlineData("navegador-x", ActivityOrigin.Api)]
    [InlineData("system", ActivityOrigin.Api)]
    public void Origin_ComesFromTheClientHeader_AndSystemNeverComesFromOutside(string? header, ActivityOrigin expected) =>
        Assert.Equal(expected, ActivityOrigins.FromClientHeader(header));

    [Fact]
    public void Event_KeepsTheActor_CutsLongSummaries_AndRequiresTheEssentials()
    {
        var activity = new ActivityEvent(OrganizationId, Person, ActivityActions.TaskUpdated, ActivityEntityTypes.Task, Guid.CreateVersion7(), new string('x', 600), Now)
            .InProject(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Equal((Person.ActorId, ActorKind.User, ActivityOrigin.Web, "0HN-REQ"), (activity.ActorId, activity.ActorKind, activity.Origin, activity.RequestId));
        Assert.Equal(ActivityEvent.SummaryMaxLength, activity.Summary.Length);
        Assert.NotNull(activity.TaskId);
        Assert.Throws<ArgumentException>(() => new ActivityEvent(Guid.Empty, Person, "task.updated", "task", Guid.CreateVersion7(), "Resumo", Now));
        Assert.Throws<ArgumentException>(() => new ActivityEvent(OrganizationId, Person, " ", "task", Guid.CreateVersion7(), "Resumo", Now));
        Assert.Throws<ArgumentException>(() => new ActivityEvent(OrganizationId, Person, "task.updated", "task", Guid.Empty, "Resumo", Now));
        Assert.Throws<ArgumentException>(() => new ActivityEvent(OrganizationId, Person, "task.updated", "task", Guid.CreateVersion7(), " ", Now));
    }

    [Fact]
    public void Event_BoundsTheRequestId_AndSystemHasNoAuthor()
    {
        var longRequest = Person with { RequestId = new string('r', 300) };

        var activity = new ActivityEvent(OrganizationId, longRequest, "task.updated", "task", Guid.CreateVersion7(), "Resumo", Now);

        Assert.Equal(ActivityActor.RequestIdMaxLength, activity.RequestId!.Length);
        Assert.Null(ActivityActor.System.ActorId);
        Assert.Equal(ActivityOrigin.System, ActivityActor.System.Origin);
    }

    [Fact]
    public void ManualLog_CannotBeInTheFuture_AndValidatesItsTexts()
    {
        var projectId = Guid.CreateVersion7();

        var log = new ActivityLog(OrganizationId, projectId, null, Person, ActivityLogKind.Meeting, "  Revisão  ", "  ", Now.AddDays(-1), Now);

        Assert.Equal(("Revisão", null), (log.Summary, log.Details));
        Assert.Equal(Person.ActorId, log.AuthorId);
        Assert.Throws<ArgumentException>(() => new ActivityLog(OrganizationId, projectId, null, Person, ActivityLogKind.Note, "Futuro", null, Now.AddSeconds(1), Now));
        Assert.Throws<ArgumentException>(() => new ActivityLog(OrganizationId, projectId, null, Person, ActivityLogKind.Note, new string('x', 501), null, Now, Now));
        Assert.Throws<ArgumentException>(() => new ActivityLog(OrganizationId, projectId, null, Person, ActivityLogKind.Note, "Ok", new string('x', 10_001), Now, Now));
    }

    [Fact]
    public void Changes_RecordFromAndTo_ContentOnlyAsChanged_AndListDifferences()
    {
        var json = new ActivityChanges()
            .Set("title", "Antes", "Depois")
            .Set("priority", ActorKind.User, ActorKind.Agent)
            .Set("dueDate", null, new DateOnly(2026, 12, 1))
            .Set("unchanged", "igual", "igual")
            .ContentChanged("description")
            .List("labels", ["web", "api"], ["web", "mobile"])
            .List("tags", ["a"], ["a"])
            .ToJson();

        using var document = JsonDocument.Parse(json!);
        var root = document.RootElement;

        Assert.Equal(["description", "dueDate", "labels", "priority", "title"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Depois", root.GetProperty("title").GetProperty("to").GetString());
        Assert.Equal("Agent", root.GetProperty("priority").GetProperty("to").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("dueDate").GetProperty("from").ValueKind);
        Assert.Equal("2026-12-01", root.GetProperty("dueDate").GetProperty("to").GetString());
        Assert.True(root.GetProperty("description").GetProperty("changed").GetBoolean());
        Assert.Equal(["mobile"], root.GetProperty("labels").GetProperty("added").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(["api"], root.GetProperty("labels").GetProperty("removed").EnumerateArray().Select(item => item.GetString()));
        Assert.Null(new ActivityChanges().Set("x", 1, 1).ToJson());
    }
}
