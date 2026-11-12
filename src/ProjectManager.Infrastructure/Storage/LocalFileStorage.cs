using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ProjectManager.Application.Abstractions.Storage;

namespace ProjectManager.Infrastructure.Storage;

/// <summary>Configuração do armazenamento de anexos em disco local.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage:Attachments";

    /// <summary>
    /// Diretório raiz, fora do <c>wwwroot</c> e do repositório. Aceita variáveis de ambiente
    /// (ex.: <c>%LOCALAPPDATA%</c>). Obrigatório.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    public string ResolvedRootPath => Path.GetFullPath(Environment.ExpandEnvironmentVariables(RootPath));
}

/// <summary>
/// Arquivos em disco local, sob um diretório raiz (ADR-011). A chave só aceita letras minúsculas,
/// números e barras, e o caminho final é conferido contra a raiz: não há como sair dela.
/// </summary>
internal sealed partial class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _root = EnsureTrailingSeparator(options.Value.ResolvedRootPath);

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Grava num arquivo temporário e move: um arquivo pela metade nunca fica no caminho final.
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        File.Delete(ResolvePath(key));
        return Task.CompletedTask;
    }

    private string ResolvePath(string key)
    {
        if (string.IsNullOrEmpty(key) || !KeyPattern().IsMatch(key))
        {
            throw new ArgumentException("Chave de armazenamento inválida.", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));

        return path.StartsWith(_root, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException("Chave de armazenamento fora do diretório raiz.", nameof(key));
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    [GeneratedRegex("^[a-z0-9]+(/[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
