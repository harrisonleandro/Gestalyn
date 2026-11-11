namespace ProjectManager.Web.Api;

public sealed record ApiAcceptedInvitation(Guid OrganizationId, string OrganizationName, string RoleName);

/// <summary>Cliente HTTP tipado para a aceitação de convites (<c>/api/v1/invitations</c>).</summary>
public sealed class ApiInvitationClient(HttpClient httpClient, ILogger<ApiInvitationClient> logger)
{
    public Task<ApiResult<ApiAcceptedInvitation>> AcceptAsync(string accessToken, string token, CancellationToken cancellationToken) =>
        ApiRequests.SendAsync<ApiAcceptedInvitation>(
            httpClient,
            logger,
            HttpMethod.Post,
            "api/v1/invitations/accept",
            new { token },
            accessToken,
            cancellationToken);
}
