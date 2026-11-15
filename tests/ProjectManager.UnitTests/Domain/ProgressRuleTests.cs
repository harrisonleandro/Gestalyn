using ProjectManager.Application.Dashboards;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;
using ProjectManager.Infrastructure.Dashboards;

namespace ProjectManager.UnitTests.Domain;

/// <summary>Regra de progresso e estatísticas (ADR-014, D7 e D8).</summary>
public sealed class ProgressRuleTests
{
    [Theory]
    [InlineData(1, 1, 1, 2, 1, 40.0)]
    [InlineData(0, 0, 0, 3, 0, 100.0)]
    [InlineData(2, 0, 1, 0, 0, 0.0)]
    [InlineData(1, 0, 0, 2, 0, 66.7)]
    [InlineData(0, 0, 0, 1, 2, 100.0)]
    public void Percent_IsDoneOverNotCanceled(int todo, int inProgress, int blocked, int done, int canceled, double expected)
    {
        var progress = ProgressRule.Calculate(new CategoryCounts(todo, inProgress, blocked, done, canceled));

        Assert.Equal(expected, progress.Percent);
        Assert.Equal("doneOverNotCanceled", progress.Rule);
        Assert.Equal(todo + inProgress + blocked + done + canceled, progress.Counts.Total);
    }

    [Fact]
    public void WithoutTasksToCount_ProgressIsUnknown_NeverZeroOrOneHundred()
    {
        Assert.Null(ProgressRule.Calculate(CategoryCounts.Empty).Percent);
        Assert.Null(ProgressRule.Calculate(new CategoryCounts(0, 0, 0, 0, 3)).Percent);
    }

    [Fact]
    public void Counts_AreGroupedByCategory()
    {
        var counts = CategoryCounts.From([(StateCategory.Todo, 2), (StateCategory.Todo, 1), (StateCategory.Done, 4), (StateCategory.Blocked, 1)]);

        Assert.Equal(new CategoryCounts(3, 0, 1, 4, 0), counts);
    }

    [Fact]
    public void CycleTime_UsesTheMedianAndTheNearestRank85thPercentile()
    {
        Assert.Equal(new CycleTime(0, null, null), DashboardService.CycleTimeOf([]));
        Assert.Equal(new CycleTime(3, 2.0, 4.0), DashboardService.CycleTimeOf([1.0, 2.0, 4.0]));
        Assert.Equal(new CycleTime(4, 2.5, 10.0), DashboardService.CycleTimeOf([1.0, 2.0, 3.0, 10.0]));
        Assert.Equal(new CycleTime(1, 0.3, 0.3), DashboardService.CycleTimeOf([0.25]));
    }

    [Theory]
    [InlineData("2026-11-16", "2026-11-16")]
    [InlineData("2026-11-15", "2026-11-09")]
    [InlineData("2026-11-19", "2026-11-16")]
    [InlineData("2026-11-22", "2026-11-16")]
    public void Weeks_RunFromMondayToSunday(string date, string monday) =>
        Assert.Equal(DateOnly.Parse(monday, System.Globalization.CultureInfo.InvariantCulture), DashboardService.WeekStart(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)));
}
