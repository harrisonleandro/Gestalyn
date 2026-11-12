using System.Text;

namespace ProjectManager.Domain.Tasks;

/// <summary>Tipo de arquivo aceito como anexo, com o tipo de conteúdo que o servidor informa no download.</summary>
public sealed record AttachmentFileType(string Extension, string ContentType);

/// <summary>
/// Regras de arquivos anexados (ADR-011): lista de tipos permitidos, conferida pela extensão e pelo
/// conteúdo (assinatura do arquivo ou texto UTF-8 válido), e limpeza do nome enviado. SVG, HTML e
/// executáveis nunca são aceitos.
/// </summary>
public static class AttachmentFiles
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;
    public const int FileNameMaxLength = 255;
    public const string DefaultFileName = "arquivo";

    public const string NotAllowedMessage =
        "Tipo de arquivo não permitido. Envie PNG, JPEG, GIF, WebP, PDF, TXT, MD, CSV, JSON, LOG, DOCX, XLSX ou PPTX.";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Gif87 = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89 = "GIF89a"u8.ToArray();
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();
    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Dictionary<string, (AttachmentFileType Type, Func<ReadOnlyMemory<byte>, bool> Matches)> Allowed =
        new(StringComparer.Ordinal)
        {
            [".png"] = (new(".png", "image/png"), content => StartsWith(content, Png)),
            [".jpg"] = (new(".jpg", "image/jpeg"), content => StartsWith(content, Jpeg)),
            [".jpeg"] = (new(".jpeg", "image/jpeg"), content => StartsWith(content, Jpeg)),
            [".gif"] = (new(".gif", "image/gif"), content => StartsWith(content, Gif87) || StartsWith(content, Gif89)),
            [".webp"] = (new(".webp", "image/webp"), content => StartsWith(content, Riff) && content.Length >= 12 && content.Span[8..12].SequenceEqual(Webp)),
            [".pdf"] = (new(".pdf", "application/pdf"), content => StartsWith(content, Pdf)),
            [".docx"] = (new(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"), content => StartsWith(content, Zip)),
            [".xlsx"] = (new(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"), content => StartsWith(content, Zip)),
            [".pptx"] = (new(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"), content => StartsWith(content, Zip)),
            [".txt"] = (new(".txt", "text/plain; charset=utf-8"), IsUtf8Text),
            [".log"] = (new(".log", "text/plain; charset=utf-8"), IsUtf8Text),
            [".md"] = (new(".md", "text/markdown; charset=utf-8"), IsUtf8Text),
            [".csv"] = (new(".csv", "text/csv; charset=utf-8"), IsUtf8Text),
            [".json"] = (new(".json", "application/json; charset=utf-8"), IsUtf8Text),
        };

    /// <summary>
    /// Identifica o tipo pelo nome (já limpo) e confere o conteúdo. Retorna <c>null</c> quando a extensão
    /// não é permitida ou o conteúdo não corresponde a ela (por exemplo, um executável renomeado para .png).
    /// </summary>
    public static AttachmentFileType? Identify(string fileName, ReadOnlyMemory<byte> content)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return content.Length > 0 && Allowed.TryGetValue(extension, out var entry) && entry.Matches(content)
            ? entry.Type
            : null;
    }

    /// <summary>
    /// Nome seguro para guardar e devolver no download: sem diretórios, caracteres de controle ou
    /// caracteres reservados, sem pontos ou espaços nas pontas e com no máximo 255 caracteres, preservando
    /// a extensão. O nome nunca é usado para gravar o arquivo no disco.
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];

        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (!char.IsControl(character) && "<>:\"|?*".IndexOf(character, StringComparison.Ordinal) < 0)
            {
                builder.Append(character);
            }
        }

        // A extensão é separada antes de tirar pontos e espaços das pontas: ".pdf" vira "arquivo.pdf".
        name = builder.ToString().Trim().TrimEnd('.', ' ');
        var extension = Path.GetExtension(name);
        var baseName = name[..^extension.Length].Trim().Trim('.').Trim();
        name = (baseName.Length == 0 ? DefaultFileName : baseName) + extension;

        if (name.Length > FileNameMaxLength)
        {
            var keep = extension.Length < 20 ? extension : string.Empty;
            name = name[..(FileNameMaxLength - keep.Length)] + keep;
        }

        return name;
    }

    private static bool StartsWith(ReadOnlyMemory<byte> content, byte[] signature) =>
        content.Span.StartsWith(signature);

    /// <summary>Texto UTF-8 válido, sem bytes nulos (que indicariam um arquivo binário).</summary>
    private static bool IsUtf8Text(ReadOnlyMemory<byte> content)
    {
        if (content.Span.IndexOf((byte)0) >= 0)
        {
            return false;
        }

        try
        {
            _ = StrictUtf8.GetCharCount(content.Span);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
