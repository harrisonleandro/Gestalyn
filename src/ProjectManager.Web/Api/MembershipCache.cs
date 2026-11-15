namespace ProjectManager.Web.Api;

/// <summary>
/// Papel e permissões do usuário numa organização, pedidos uma vez por requisição: o menu do layout e a página usam a
/// mesma resposta de <c>GET /organizations/{id}/me</c>.
/// </summary>
public sealed class MembershipCache(OrganizationsApi organizations)
{
    private readonly Dictionary<Guid, Task<ApiResult<MyMembership>>> _requests = [];

    public Task<ApiResult<MyMembership>> GetAsync(Guid organizationId)
    {
        if (!_requests.TryGetValue(organizationId, out var request))
        {
            request = organizations.GetMineAsync(organizationId);
            _requests[organizationId] = request;
        }

        return request;
    }
}
