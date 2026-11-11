using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Application.Organizations;
using ProjectManager.IntegrationTests.Authentication;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Organizations;

/// <summary>Usuário real (cadastrado, com e-mail confirmado e sessão aberta).</summary>
internal sealed record TestUser(Guid Id, string Email, string AccessToken);

/// <summary>
/// Operações de organização pela API, com usuários reais e convites lidos do Mailpit.
/// </summary>
internal sealed class OrganizationTestKit(HttpClient client, MailpitFixture mailpit)
{
    public const string InvitationSubject = "Convite para";

    private readonly AuthTestClient _auth = new(client, mailpit);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async Task<TestUser> CreateUserAsync()
    {
        var email = AuthTestClient.NewEmail();
        var tokens = await _auth.CreateConfirmedUserAndLoginAsync(email);

        using var me = await _auth.MeAsync(tokens.AccessToken);
        var profile = await me.Content.ReadFromJsonAsync<UserProfile>(CancellationToken);

        return new TestUser(profile!.Id, email, tokens.AccessToken);
    }

    public async Task<OrganizationSummary> CreateOrganizationAsync(TestUser user, string name = "Organização de teste")
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/organizations", user, new { name });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<OrganizationSummary>(CancellationToken))!;
    }

    public async Task<Guid> RoleIdAsync(TestUser user, Guid organizationId, string systemRole)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/v1/organizations/{organizationId}/roles", user);
        response.EnsureSuccessStatusCode();
        var roles = await response.Content.ReadFromJsonAsync<List<RoleDetails>>(CancellationToken);

        return roles!.Single(role => role.SystemRole == systemRole).Id;
    }

    public Task<HttpResponseMessage> InviteAsync(TestUser inviter, Guid organizationId, string email, Guid roleId) =>
        SendAsync(HttpMethod.Post, $"/api/v1/organizations/{organizationId}/invitations", inviter, new { email, roleId });

    /// <summary>Token do convite mais recente recebido pelo e-mail informado.</summary>
    public async Task<string> InvitationTokenAsync(string email) =>
        (await mailpit.WaitForEmailAsync(email, InvitationSubject, CancellationToken)).LinkParameter("token");

    public Task<HttpResponseMessage> AcceptAsync(TestUser user, string token) =>
        SendAsync(HttpMethod.Post, "/api/v1/invitations/accept", user, new { token });

    /// <summary>Convida e faz o convidado aceitar, com o papel de sistema informado.</summary>
    public async Task AddMemberAsync(TestUser owner, Guid organizationId, TestUser invitee, string systemRole)
    {
        var roleId = await RoleIdAsync(owner, organizationId, systemRole);
        using (var invitation = await InviteAsync(owner, organizationId, invitee.Email, roleId))
        {
            invitation.EnsureSuccessStatusCode();
        }

        using var acceptance = await AcceptAsync(invitee, await InvitationTokenAsync(invitee.Email));
        acceptance.EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, TestUser? user, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (user is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(CancellationToken))!;
}
