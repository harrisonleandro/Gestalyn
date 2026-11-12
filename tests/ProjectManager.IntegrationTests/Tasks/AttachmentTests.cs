using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ProjectManager.Application.Tasks;
using ProjectManager.Domain.Tasks;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Projects;

namespace ProjectManager.IntegrationTests.Tasks;

/// <summary>
/// Anexos protegidos (ADR-011): tipos conferidos pelo conteúdo, limite de tamanho, nome neutralizado,
/// arquivo gravado só com o ID e download apenas pela API, com cabeçalhos de proteção.
/// </summary>
public sealed class AttachmentTests : IDisposable
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly TaskTestKit _kit;

    public AttachmentTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new TaskTestKit(_client, mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upload_StoresTheFileUnderItsId_AndDownloadIsAlwaysAnAttachment()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var task = await _kit.CreateTaskAsync(workspace, "Com anexos");

        using var upload = await UploadAsync(workspace, task.Id, workspace.Owner, "..\\..\\tela<inicial>.PNG", PngBytes);
        var attachment = await TaskTestKit.ReadAsync<AttachmentDetails>(upload);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Equal(("telainicial.PNG", "image/png", (long)PngBytes.Length), (attachment.FileName, attachment.ContentType, attachment.SizeBytes));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(PngBytes)), attachment.Sha256);

        // No disco, o arquivo tem só o ID como nome, dentro da raiz configurada.
        var stored = Assert.Single(Directory.GetFiles(_factory.AttachmentsRoot, "*", SearchOption.AllDirectories));
        Assert.Equal(attachment.Id.ToString("N"), Path.GetFileName(stored));
        Assert.Equal(PngBytes, await File.ReadAllBytesAsync(stored, CancellationToken));

        using var download = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, $"/attachments/{attachment.Id}/content", workspace.Owner);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(PngBytes, await download.Content.ReadAsByteArrayAsync(CancellationToken));
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("telainicial.PNG", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("sandbox", Assert.Single(download.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);

        using var list = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "/attachments", workspace.Owner);
        Assert.Equal([attachment.Id], (await TaskTestKit.ReadAsync<List<AttachmentDetails>>(list)).Select(a => a.Id));
    }

    [Fact]
    public async Task Upload_RejectsDisallowedTypes_SpoofedContent_EmptyAndOversizedFiles()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var task = await _kit.CreateTaskAsync(workspace, "Validação de arquivos");
        var executable = "MZ\u0090\0\u0003\0\0\0"u8.ToArray();

        (string Name, byte[] Content)[] rejected =
        [
            ("programa.exe", executable),
            ("imagem.png", executable),
            ("desenho.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray()),
            ("pagina.html", "<html><script>alert(1)</script></html>"u8.ToArray()),
            ("notas.txt", [0x41, 0x00, 0x42]),
            ("latin1.csv", [0x63, 0xE7, 0x61]),
            ("vazio.pdf", []),
            ("grande.txt", Enumerable.Repeat((byte)'a', (int)AttachmentFiles.MaxSizeBytes + 1).ToArray()),
        ];

        foreach (var (name, content) in rejected)
        {
            using var response = await UploadAsync(workspace, task.Id, workspace.Owner, name, content);
            Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, $"{name}: obtido {response.StatusCode}.");
        }

        using var noFile = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/attachments", workspace.Owner, new { file = "x" });
        using var text = await UploadAsync(workspace, task.Id, workspace.Owner, "leia-me.md", Encoding.UTF8.GetBytes("# Título com acentuação"));
        using var pdf = await UploadAsync(workspace, task.Id, workspace.Owner, "relatorio.pdf", "%PDF-1.7\n%"u8.ToArray());

        Assert.NotEqual(HttpStatusCode.Created, noFile.StatusCode);
        Assert.Equal("text/markdown; charset=utf-8", (await TaskTestKit.ReadAsync<AttachmentDetails>(text)).ContentType);
        Assert.Equal("application/pdf", (await TaskTestKit.ReadAsync<AttachmentDetails>(pdf)).ContentType);
        Assert.Equal(2, Directory.GetFiles(_factory.AttachmentsRoot, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Attachments_FollowProjectPermissions_AndAreInvisibleToOtherOrganizations()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var viewer = await _kit.AddTeamMemberAsync(workspace, "Viewer");
        var intruder = await _kit.Organizations.CreateUserAsync();
        var intruderOrganization = await _kit.Organizations.CreateOrganizationAsync(intruder);
        var task = await _kit.CreateTaskAsync(workspace, "Confidencial");
        using var upload = await UploadAsync(workspace, task.Id, workspace.Owner, "contrato.pdf", "%PDF-1.4 confidencial"u8.ToArray());
        var attachment = await TaskTestKit.ReadAsync<AttachmentDetails>(upload);
        var contentPath = $"/attachments/{attachment.Id}/content";

        using var viewerDownloads = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, contentPath, viewer);
        using var viewerUploads = await UploadAsync(workspace, task.Id, viewer, "outro.pdf", "%PDF-1.4"u8.ToArray());
        using var viewerDeletes = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, $"/attachments/{attachment.Id}", viewer);

        Assert.Equal(HttpStatusCode.OK, viewerDownloads.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerUploads.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerDeletes.StatusCode);

        foreach (var organizationId in new[] { intruderOrganization.Id, workspace.OrganizationId })
        {
            using var list = await _kit.SendToTaskAsync(HttpMethod.Get, task.Id, "/attachments", intruder, organizationId);
            using var download = await _kit.SendToTaskAsync(HttpMethod.Get, task.Id, contentPath, intruder, organizationId);
            using var delete = await _kit.SendToTaskAsync(HttpMethod.Delete, task.Id, $"/attachments/{attachment.Id}", intruder, organizationId);
            using var send = await UploadAsync(task.Id, organizationId, intruder, "invasao.pdf", "%PDF-1.4"u8.ToArray());

            Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
        }

        using var anonymous = await _kit.Projects.SendAsync(HttpMethod.Get, $"/api/v1/tasks/{task.Id}{contentPath}", null, organizationHeader: workspace.OrganizationId.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesTheFile_AndArchivedTasksKeepTheirAttachmentsReadOnly()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var task = await _kit.CreateTaskAsync(workspace, "Temporária");
        using var first = await UploadAsync(workspace, task.Id, workspace.Owner, "a.txt", "primeiro"u8.ToArray());
        using var second = await UploadAsync(workspace, task.Id, workspace.Owner, "b.txt", "segundo"u8.ToArray());
        var removed = await TaskTestKit.ReadAsync<AttachmentDetails>(first);
        var kept = await TaskTestKit.ReadAsync<AttachmentDetails>(second);

        using var delete = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, $"/attachments/{removed.Id}", workspace.Owner);
        using var downloadDeleted = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, $"/attachments/{removed.Id}/content", workspace.Owner);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, downloadDeleted.StatusCode);
        Assert.Equal(kept.Id.ToString("N"), Path.GetFileName(Assert.Single(Directory.GetFiles(_factory.AttachmentsRoot, "*", SearchOption.AllDirectories))));

        using var archive = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, "", workspace.Owner);
        using var uploadToArchived = await UploadAsync(workspace, task.Id, workspace.Owner, "c.txt", "terceiro"u8.ToArray());
        using var deleteFromArchived = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, $"/attachments/{kept.Id}", workspace.Owner);
        using var downloadFromArchived = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, $"/attachments/{kept.Id}/content", workspace.Owner);

        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, uploadToArchived.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deleteFromArchived.StatusCode);
        Assert.Equal("segundo", await downloadFromArchived.Content.ReadAsStringAsync(CancellationToken));
    }

    private Task<HttpResponseMessage> UploadAsync(Workspace workspace, Guid taskId, TestUser user, string fileName, byte[] content) =>
        UploadAsync(taskId, workspace.OrganizationId, user, fileName, content);

    private async Task<HttpResponseMessage> UploadAsync(Guid taskId, Guid organizationId, TestUser user, string fileName, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/tasks/{taskId}/attachments", UriKind.Relative)) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        request.Headers.TryAddWithoutValidation(ProjectTestKit.OrganizationHeader, organizationId.ToString());

        return await _client.SendAsync(request, CancellationToken);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
