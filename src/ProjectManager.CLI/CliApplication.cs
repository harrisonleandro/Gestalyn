using System.CommandLine;
using System.CommandLine.Help;

namespace ProjectManager.CLI;

/// <summary>
/// Monta e executa a árvore de comandos do <c>projectctl</c>.
/// </summary>
public static class CliApplication
{
    public const string ExecutableName = "projectctl";

    /// <summary>
    /// Cria o comando raiz. As opções <c>--help</c> e <c>--version</c> são fornecidas pelo System.CommandLine.
    /// </summary>
    /// <remarks>
    /// Os grupos de comandos (auth, organization, project, task, activity, phase, agent) serão registrados aqui,
    /// cada um em <c>Commands/&lt;Grupo&gt;/</c>, somente quando possuírem implementação real.
    /// </remarks>
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand($"{ExecutableName} — interface de linha de comando do ProjectManager.");

        // Sem subcomando, exibe a ajuda em vez de encerrar silenciosamente.
        rootCommand.SetAction(parseResult => new HelpAction().Invoke(parseResult));

        return rootCommand;
    }

    public static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var parseResult = CreateRootCommand().Parse(args);
        var configuration = new InvocationConfiguration
        {
            Output = output,
            Error = error,
        };

        return parseResult.InvokeAsync(configuration, cancellationToken);
    }
}
