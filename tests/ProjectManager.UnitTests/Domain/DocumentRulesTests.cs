using ProjectManager.Domain.Documents;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Tasks;

namespace ProjectManager.UnitTests.Domain;

/// <summary>Regras de documentos e decisões (ADR-014, D5 e D6).</summary>
public sealed class DocumentRulesTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly Guid Author = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Document_StartsAtVersionOne_AndEachEditCreatesTheNextVersion()
    {
        var document = new ProjectDocument(new Project(OrganizationId, "Projeto"), "Visão", "# Um", Author, Now);

        var second = document.Edit("Visão 2", "# Dois", expectedVersion: 1, Author, Now.AddMinutes(1));

        Assert.Equal((1, "Visão", "# Um"), (document.FirstVersion!.Number, document.FirstVersion.Title, document.FirstVersion.Body));
        Assert.Equal((2, "Visão 2", "# Dois"), (second.Number, second.Title, second.Body));
        Assert.Equal((2, "Visão 2", "# Dois"), (document.CurrentVersion, document.Title, document.Body));
        Assert.Equal(document.Id, second.DocumentId);
    }

    [Fact]
    public void Document_RejectsOutdatedEdits_ArchivedEdits_AndOversizedBodies()
    {
        var document = new ProjectDocument(new Project(OrganizationId, "Projeto"), "Visão", "", Author, Now);
        document.Edit("Visão", "v2", 1, Author, Now);

        Assert.Throws<DocumentVersionConflictException>(() => document.Edit("Visão", "v3", 1, Author, Now));
        Assert.Throws<ArgumentException>(() => document.Edit("Visão", new string('x', ProjectDocument.BodyMaxLength + 1), 2, Author, Now));
        Assert.Equal(2, document.CurrentVersion);

        document.Archive(Author, Now);
        Assert.Throws<InvalidOperationException>(() => document.Edit("Visão", "v3", 2, Author, Now));

        document.Restore();
        Assert.Equal(3, document.Edit("Visão", "v3", 2, Author, Now).Number);
    }

    [Fact]
    public void Document_CannotBeCreatedInAnArchivedProject()
    {
        var project = new Project(OrganizationId, "Projeto");
        project.Archive(Author, Now);

        Assert.Throws<InvalidOperationException>(() => new ProjectDocument(project, "Visão", "", Author, Now));
    }

    [Fact]
    public void Decision_IsEditableOnlyWhileProposed()
    {
        var decision = NewDecision(new Project(OrganizationId, "Projeto"));
        decision.ChangeAlternatives("SQLite");

        decision.Accept(Author, Now);

        Assert.Equal((DecisionStatus.Accepted, Author, Now), (decision.Status, decision.DecidedById!.Value, decision.DecidedAt!.Value));
        Assert.Throws<DecisionRuleException>(() => decision.Rename("Outro"));
        Assert.Throws<DecisionRuleException>(() => decision.ChangeOutcome("Outro"));
        Assert.Throws<DecisionRuleException>(() => decision.Reject(Author, Now));
        Assert.Throws<DecisionRuleException>(() => decision.Accept(Author, Now));
    }

    [Fact]
    public void Decision_IsSupersededOnlyByAnotherAcceptedDecisionOfTheSameProject()
    {
        var project = new Project(OrganizationId, "Projeto");
        var original = NewDecision(project);
        var proposal = NewDecision(project);
        var other = NewDecision(new Project(OrganizationId, "Outro"));

        Assert.Throws<DecisionRuleException>(() => original.SupersedeWith(proposal));
        original.Accept(Author, Now);
        Assert.Throws<DecisionRuleException>(() => original.SupersedeWith(proposal));
        Assert.Throws<DecisionRuleException>(() => original.SupersedeWith(original));
        other.Accept(Author, Now);
        Assert.Throws<DecisionRuleException>(() => original.SupersedeWith(other));

        proposal.Accept(Author, Now);
        original.SupersedeWith(proposal);

        Assert.Equal((DecisionStatus.Superseded, proposal.Id), (original.Status, original.SupersededById!.Value));
        Assert.Throws<DecisionRuleException>(() => original.SupersedeWith(proposal));
    }

    [Fact]
    public void Decision_RelatesOnlyToTasksOfItsProject()
    {
        var project = new Project(OrganizationId, "Projeto");
        var decision = NewDecision(project);
        var foreign = new ProjectTask(new Project(OrganizationId, "Outro"), 1, "Fora", Author);
        var own = new ProjectTask(project, 1, "Dentro", Author);

        Assert.Throws<ArgumentException>(() => decision.RelateToTask(foreign));
        decision.RelateToTask(own);
        Assert.Equal(own.Id, decision.TaskId);
    }

    private static Decision NewDecision(Project project) => new(project, "Banco", "Precisamos persistir", "PostgreSQL", Author);
}
