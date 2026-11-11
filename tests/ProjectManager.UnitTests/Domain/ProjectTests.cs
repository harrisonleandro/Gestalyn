using ProjectManager.Domain.Projects;

namespace ProjectManager.UnitTests.Domain;

public sealed class ProjectTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();

    [Fact]
    public void Constructor_RequiresOrganization()
    {
        Assert.Throws<ArgumentException>(() => new Project(Guid.Empty, "Projeto"));
    }

    [Fact]
    public void Constructor_NormalizesBlankDescriptionToNull()
    {
        var project = new Project(OrganizationId, " Projeto ", "   ");

        Assert.Equal("Projeto", project.Name);
        Assert.Null(project.Description);
        Assert.Equal(OrganizationId, project.OrganizationId);
    }

    [Fact]
    public void Constructor_RejectsDescriptionAboveMaxLength()
    {
        var description = new string('d', Project.DescriptionMaxLength + 1);

        Assert.Throws<ArgumentException>(() => new Project(OrganizationId, "Projeto", description));
    }

    [Fact]
    public void AddPhase_AssignsSequentialPositionsAndProjectOwnership()
    {
        var project = new Project(OrganizationId, "Projeto");

        var first = project.AddPhase("Descoberta");
        var second = project.AddPhase("Construção");

        Assert.Equal(1, first.Position);
        Assert.Equal(2, second.Position);
        Assert.All(project.Phases, phase =>
        {
            Assert.Equal(project.Id, phase.ProjectId);
            Assert.Equal(project.OrganizationId, phase.OrganizationId);
        });
        Assert.Equal(["Descoberta", "Construção"], project.Phases.Select(phase => phase.Name));
    }

    [Fact]
    public void AddPhase_RejectsInvalidName()
    {
        var project = new Project(OrganizationId, "Projeto");

        Assert.ThrowsAny<ArgumentException>(() => project.AddPhase(""));
        Assert.Empty(project.Phases);
    }
}
