using System.Reflection;

namespace ProjectManager.Web;

internal static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
