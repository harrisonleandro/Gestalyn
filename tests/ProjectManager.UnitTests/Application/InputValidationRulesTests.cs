using System.ComponentModel.DataAnnotations;
using ProjectManager.Application.Common;
using ProjectManager.Application.Projects;
using ProjectManager.Application.Tasks;

namespace ProjectManager.UnitTests.Application;

/// <summary>Validação de entrada: texto em branco nos campos de PATCH e limites da paginação.</summary>
public sealed class InputValidationRulesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("Nome", true)]
    [InlineData(" Nome ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("\t\r\n", false)]
    [InlineData("  ", false)]
    public void NotWhiteSpace_AcceptsAbsentOrFilledText_AndRejectsBlankText(string? value, bool valid)
    {
        Assert.Equal(valid, new NotWhiteSpaceAttribute().IsValid(value));
    }

    [Fact]
    public void PatchRequests_RejectBlankNames_LikeTheCreateRequests()
    {
        var project = new UpdateProjectRequest { Name = "   " };
        var task = new UpdateTaskRequest { Title = "\t" };
        var untouched = new UpdateProjectRequest();

        Assert.Contains(Validate(project), result => result.MemberNames.Contains(nameof(UpdateProjectRequest.Name)));
        Assert.Contains(Validate(task), result => result.MemberNames.Contains(nameof(UpdateTaskRequest.Title)));
        Assert.Empty(Validate(untouched));
    }

    [Fact]
    public void MaxPage_IsTheLargestPageWhoseOffsetFitsAnInt()
    {
        Assert.Equal(21_474_837, PageQuery.MaxPage);
        Assert.True((long)(PageQuery.MaxPage - 1) * PageQuery.MaxPageSize <= int.MaxValue);
        Assert.True((long)PageQuery.MaxPage * PageQuery.MaxPageSize > int.MaxValue);
    }

    [Theory]
    [InlineData(1, 50, 0)]
    [InlineData(3, 20, 40)]
    [InlineData(PageQuery.MaxPage, PageQuery.MaxPageSize, 2_147_483_600)]
    public void Offset_IsComputedWithoutOverflowWithinTheLimits(int page, int pageSize, int expected)
    {
        Assert.Equal(expected, new ProjectListQuery { Page = page, PageSize = pageSize }.CalculateOffset());
    }

    [Fact]
    public void Offset_FailsLoudlyInsteadOfGoingNegative_WhenValidationIsBypassed()
    {
        var query = new TaskListQuery { Page = int.MaxValue, PageSize = PageQuery.MaxPageSize };

        Assert.Throws<OverflowException>(() => query.CalculateOffset());
        Assert.Contains(Validate(query), result => result.MemberNames.Contains(nameof(PageQuery.Page)));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
