using System.Reflection;
using ProjectManager.CLI;

namespace ProjectManager.UnitTests.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task Version_PrintsInformationalVersionAndSucceeds()
    {
        var result = await RunAsync("--version");

        // Em um repositório Git, o SDK acrescenta o commit à versão (ex.: "0.1.0+<sha>").
        var informationalVersion = typeof(CliApplication).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(informationalVersion, result.Output.Trim());
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task Help_DescribesCliAndSucceeds(string helpOption)
    {
        var result = await RunAsync(helpOption);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(CliApplication.ExecutableName, result.Output, StringComparison.Ordinal);
        Assert.Contains("--version", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task NoArguments_ShowsHelpAndSucceeds()
    {
        var result = await RunAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--help", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownCommand_FailsWithNonZeroExitCode()
    {
        var result = await RunAsync("comando-inexistente");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("comando-inexistente", result.Error, StringComparison.Ordinal);
    }

    private static async Task<CliResult> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(args, output, error, TestContext.Current.CancellationToken);

        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);
}
