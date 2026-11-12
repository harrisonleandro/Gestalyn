using System.Text;
using ProjectManager.Domain.Tasks;

namespace ProjectManager.UnitTests.Domain;

/// <summary>Tipos permitidos conferidos pelo conteúdo e limpeza do nome dos anexos (ADR-011).</summary>
public sealed class AttachmentFilesTests
{
    public static TheoryData<string, byte[], string> Accepted => new()
    {
        { "foto.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01], "image/png" },
        { "foto.JPG", [0xFF, 0xD8, 0xFF, 0xE0, 0x00], "image/jpeg" },
        { "anim.gif", "GIF89a.."u8.ToArray(), "image/gif" },
        { "img.webp", "RIFF\0\0\0\0WEBPVP8 "u8.ToArray(), "image/webp" },
        { "doc.pdf", "%PDF-1.7"u8.ToArray(), "application/pdf" },
        { "planilha.xlsx", [0x50, 0x4B, 0x03, 0x04, 0x14], "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { "dados.json", "{\"ação\": 1}"u8.ToArray(), "application/json; charset=utf-8" },
        { "notas.txt", Encoding.UTF8.GetBytes("Olá, mundo"), "text/plain; charset=utf-8" },
    };

    public static TheoryData<string, byte[]> Rejected => new()
    {
        { "programa.exe", "MZ\u0090\0"u8.ToArray() },
        { "foto.png", "MZ\u0090\0"u8.ToArray() },
        { "foto.png", [0xFF, 0xD8, 0xFF, 0xE0] },
        { "desenho.svg", "<svg></svg>"u8.ToArray() },
        { "pagina.html", "<html></html>"u8.ToArray() },
        { "script.js", "alert(1)"u8.ToArray() },
        { "sem-extensao", "%PDF-1.7"u8.ToArray() },
        { "binario.txt", [0x41, 0x00, 0x42] },
        { "latin1.csv", [0x63, 0xE7, 0x61] },
        { "falso.webp", "RIFF\0\0\0\0WAVEfmt "u8.ToArray() },
        { "vazio.pdf", [] },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void Identify_AcceptsAllowedTypesWithMatchingContent(string fileName, byte[] content, string expectedContentType)
    {
        Assert.Equal(expectedContentType, AttachmentFiles.Identify(fileName, content)?.ContentType);
    }

    [Theory]
    [MemberData(nameof(Rejected))]
    public void Identify_RejectsOtherTypesAndSpoofedContent(string fileName, byte[] content)
    {
        Assert.Null(AttachmentFiles.Identify(fileName, content));
    }

    [Theory]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("..\\..\\Windows\\win.ini.txt", "win.ini.txt")]
    [InlineData("rela<tó>rio:final?.pdf", "relatóriofinal.pdf")]
    [InlineData("  .oculto.txt.  ", "oculto.txt")]
    [InlineData("nome\u0000com\u001Fcontrole.md", "nomecomcontrole.md")]
    [InlineData("", "arquivo")]
    [InlineData(null, "arquivo")]
    [InlineData(".pdf", "arquivo.pdf")]
    public void SanitizeFileName_RemovesPathsAndUnsafeCharacters(string? input, string expected)
    {
        Assert.Equal(expected, AttachmentFiles.SanitizeFileName(input));
    }

    [Fact]
    public void SanitizeFileName_LimitsTheLengthAndKeepsTheExtension()
    {
        var name = AttachmentFiles.SanitizeFileName(new string('a', 400) + ".pdf");

        Assert.Equal(AttachmentFiles.FileNameMaxLength, name.Length);
        Assert.EndsWith(".pdf", name, StringComparison.Ordinal);
    }
}
