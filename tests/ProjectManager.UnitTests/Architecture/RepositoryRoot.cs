namespace ProjectManager.UnitTests.Architecture;

internal static class RepositoryRoot
{
    private const string SolutionFileName = "ProjectManager.sln";

    public static string Path { get; } = Locate();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Não foi possível localizar {SolutionFileName} a partir de {AppContext.BaseDirectory}.");
    }
}
