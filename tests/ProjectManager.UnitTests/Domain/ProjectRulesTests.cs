using ProjectManager.Application.Abstractions.MultiTenancy;
using ProjectManager.Application.Projects;
using ProjectManager.Domain.Organizations;
using ProjectManager.Domain.Projects;

namespace ProjectManager.UnitTests.Domain;

/// <summary>Regras de projeto da Fase 05: datas, etiquetas, repositórios, arquivamento e permissões.</summary>
public sealed class ProjectRulesTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();

    [Fact]
    public void NewProject_HasMediumPriority_AndIsNotArchived()
    {
        var project = new Project(OrganizationId, "Projeto");

        Assert.Equal(ProjectPriority.Medium, project.Priority);
        Assert.False(project.IsArchived);
    }

    [Fact]
    public void Schedule_AcceptsSameDay_AndRejectsDueDateBeforeStart()
    {
        var project = new Project(OrganizationId, "Projeto");
        var day = new DateOnly(2027, 1, 10);

        project.Schedule(day, day);
        Assert.Throws<ArgumentException>(() => project.Schedule(day, day.AddDays(-1)));

        Assert.Equal((day, day), (project.StartDate, project.DueDate));
    }

    [Fact]
    public void SetTags_UnifiesCaseInsensitively_KeepsTheFirstSpelling_AndReplacesTheSet()
    {
        var project = new Project(OrganizationId, "Projeto");

        project.SetTags([" Web ", "WEB", "api"]);
        Assert.Equal(["api", "Web"], project.Tags.Select(tag => tag.Name));

        project.SetTags(["web", "Mobile"]);
        Assert.Equal(["Mobile", "web"], project.Tags.Select(tag => tag.Name));
        Assert.All(project.Tags, tag => Assert.Equal((OrganizationId, project.Id), (tag.OrganizationId, tag.ProjectId)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123456789012345678901")]
    public void SetTags_RejectsBlankOrTooLongNames(string name)
    {
        var project = new Project(OrganizationId, "Projeto");

        Assert.Throws<ArgumentException>(() => project.SetTags([name]));
        Assert.Empty(project.Tags);
    }

    [Fact]
    public void SetTags_RejectsMoreThanTheLimit_CountingDuplicatesOnce()
    {
        var project = new Project(OrganizationId, "Projeto");
        var limit = Enumerable.Range(1, ProjectTag.MaxPerProject).Select(i => $"t{i}").ToList();

        project.SetTags([.. limit, "T1"]);
        Assert.Throws<ArgumentException>(() => project.SetTags([.. limit, "extra"]));

        Assert.Equal(ProjectTag.MaxPerProject, project.Tags.Count);
    }

    [Theory]
    [InlineData("https://GitHub.com/Gestalyn/Api.git/", "https://github.com/Gestalyn/Api", RepositoryProvider.GitHub)]
    [InlineData("https://gitlab.com/time/app", "https://gitlab.com/time/app", RepositoryProvider.GitLab)]
    [InlineData("https://bitbucket.org/time/app/", "https://bitbucket.org/time/app", RepositoryProvider.Bitbucket)]
    [InlineData("https://git.exemplo.com.br:8443/time/app", "https://git.exemplo.com.br:8443/time/app", RepositoryProvider.Other)]
    public void RepositoryUrl_IsNormalized_AndProviderInferred(string input, string expectedUrl, RepositoryProvider expectedProvider)
    {
        Assert.True(ProjectRepository.TryNormalizeUrl(input, out var url, out _));

        var repository = new Project(OrganizationId, "Projeto").AddRepository(url, "Repositório");

        Assert.Equal(expectedUrl, repository.Url);
        Assert.Equal(expectedProvider, repository.Provider);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("github.com/gestalyn/api")]
    [InlineData("http://github.com/gestalyn/api")]
    [InlineData("ssh://git@github.com/gestalyn/api")]
    [InlineData("https://usuario:senha@github.com/gestalyn/api")]
    [InlineData("https://github.com/gestalyn/api?token=abc")]
    [InlineData("https://github.com/gestalyn/api#readme")]
    public void RepositoryUrl_RejectsAnythingButCleanHttps(string? input)
    {
        Assert.False(ProjectRepository.TryNormalizeUrl(input, out var url, out var error));
        Assert.Null(url);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void AddRepository_RejectsDuplicates()
    {
        var project = new Project(OrganizationId, "Projeto");
        Assert.True(ProjectRepository.TryNormalizeUrl("https://github.com/a/b", out var url, out _));
        Assert.True(ProjectRepository.TryNormalizeUrl("https://github.com/a/b.git", out var sameUrl, out _));

        project.AddRepository(url, null);

        Assert.Throws<InvalidOperationException>(() => project.AddRepository(sameUrl, null));
    }

    [Fact]
    public void ArchivedProject_IsReadOnly_AndArchivingAgainKeepsTheFirstRecord()
    {
        var project = new Project(OrganizationId, "Projeto");
        var userId = Guid.CreateVersion7();
        var archivedAt = new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero);

        project.Archive(userId, archivedAt);
        project.Archive(Guid.CreateVersion7(), archivedAt.AddDays(1));

        Assert.Equal((archivedAt, userId), (project.ArchivedAt, project.ArchivedByUserId));
        Assert.Throws<InvalidOperationException>(() => project.Rename("Outro"));
        Assert.Throws<InvalidOperationException>(() => project.SetTags(["x"]));
        Assert.Throws<InvalidOperationException>(() => project.ChangePriority(ProjectPriority.High));
        Assert.Throws<InvalidOperationException>(() => project.AddPhase("Fase"));
    }

    [Fact]
    public void ProjectRoles_GrantTheirPermissions()
    {
        Assert.Equal(ProjectPermissions.All, ProjectPermissions.For(ProjectRole.Manager));
        Assert.Equal([ProjectPermissions.Read, ProjectPermissions.Update], ProjectPermissions.For(ProjectRole.Contributor));
        Assert.Equal([ProjectPermissions.Read], ProjectPermissions.For(ProjectRole.Viewer));
    }

    [Fact]
    public void ProjectMember_InheritsTheProjectOrganization()
    {
        var project = new Project(OrganizationId, "Projeto");

        var member = new ProjectMember(project, Guid.CreateVersion7(), ProjectRole.Contributor);

        Assert.Equal((OrganizationId, project.Id), (member.OrganizationId, member.ProjectId));
        Assert.False(member.IsResponsible);
        Assert.Throws<ArgumentOutOfRangeException>(() => member.ChangeRole((ProjectRole)99));
    }

    [Fact]
    public void AccessGrant_CombinesOrganizationAndTeamPermissions()
    {
        var projectId = Guid.CreateVersion7();

        var manageAll = ProjectAccessGrant.Compute(Membership(OrganizationPermissions.ProjectsManageAll), projectId, role: null);
        var readAll = ProjectAccessGrant.Compute(Membership(OrganizationPermissions.ProjectsReadAll), projectId, role: null);
        var readAllAndContributor = ProjectAccessGrant.Compute(Membership(OrganizationPermissions.ProjectsReadAll), projectId, ProjectRole.Contributor);
        var outsideTheTeam = ProjectAccessGrant.Compute(Membership(OrganizationPermissions.ProjectsCreate), projectId, role: null);
        var viewer = ProjectAccessGrant.Compute(Membership(), projectId, ProjectRole.Viewer);

        Assert.Equal(ProjectPermissions.All.Order(StringComparer.Ordinal), manageAll!.Permissions.Order(StringComparer.Ordinal));
        Assert.Equal([ProjectPermissions.Read], readAll!.Permissions);
        Assert.True(readAllAndContributor!.HasPermission(ProjectPermissions.Update));
        Assert.False(readAllAndContributor.HasPermission(ProjectPermissions.Archive));
        Assert.Null(outsideTheTeam);
        Assert.Equal((ProjectRole?)ProjectRole.Viewer, viewer!.Role);
    }

    [Fact]
    public void SystemRoles_GrantProjectPermissions()
    {
        var roles = Role.CreateSystemRoles(OrganizationId).ToDictionary(role => role.SystemRole!.Value);

        Assert.All(
            [OrganizationPermissions.ProjectsCreate, OrganizationPermissions.ProjectsReadAll, OrganizationPermissions.ProjectsManageAll],
            code =>
            {
                Assert.True(roles[SystemRole.Owner].Grants(code));
                Assert.True(roles[SystemRole.Administrator].Grants(code));
                Assert.False(roles[SystemRole.Viewer].Grants(code));
            });
        Assert.True(roles[SystemRole.Member].Grants(OrganizationPermissions.ProjectsCreate));
        Assert.False(roles[SystemRole.Member].Grants(OrganizationPermissions.ProjectsReadAll));
        Assert.False(roles[SystemRole.Member].Grants(OrganizationPermissions.ProjectsManageAll));
    }

    private static ActiveMembership Membership(params string[] permissions) =>
        new(OrganizationId, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), IsOwner: false, permissions.ToHashSet(StringComparer.Ordinal));
}
