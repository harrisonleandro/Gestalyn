namespace ProjectManager.Web.Authentication;

/// <summary>
/// Aceita apenas URLs locais como destino após login ou logout (evita open redirect).
/// </summary>
internal static class ReturnUrls
{
    public static string Sanitize(string? returnUrl) =>
        IsLocal(returnUrl) ? returnUrl! : "/";

    private static bool IsLocal(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && Uri.IsWellFormedUriString(url, UriKind.Relative);
}
