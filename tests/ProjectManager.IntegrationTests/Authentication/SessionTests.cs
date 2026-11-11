using System.Net;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Authentication;

/// <summary>
/// Sessão: logout imediato, rotação de refresh token, detecção de reuso e rejeição de tokens inválidos.
/// </summary>
public sealed class SessionTests : IDisposable
{
    private readonly PostgresDatabaseFixture _database;
    private readonly MailpitFixture _mailpit;
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly AuthTestClient _auth;

    public SessionTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _mailpit = mailpit;

        // Sem janela de tolerância: qualquer reuso de refresh token é tratado como roubo.
        _factory = new ApiFactory(
            database.ConnectionString,
            mailpit,
            new Dictionary<string, string?> { ["Authentication:Jwt:RefreshReuseGracePeriod"] = "00:00:00" });
        _client = _factory.CreateClient();
        _auth = new AuthTestClient(_client, mailpit);
    }

    [Fact]
    public async Task ConcurrentRefreshWithinGracePeriod_DoesNotRevokeTheSession()
    {
        using var factory = new ApiFactory(_database.ConnectionString, _mailpit);
        using var client = factory.CreateClient();
        var auth = new AuthTestClient(client, _mailpit);
        var tokens = await auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());

        using var first = await auth.RefreshAsync(tokens.RefreshToken);
        var renewed = await AuthTestClient.ReadTokensAsync(first);

        // Segunda requisição paralela com o mesmo token, dentro da janela padrão de 30 s.
        using (var concurrent = await auth.RefreshAsync(tokens.RefreshToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, concurrent.StatusCode);
        }

        using var me = await auth.MeAsync(renewed.AccessToken);
        using var next = await auth.RefreshAsync(renewed.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task Logout_InvalidatesAccessAndRefreshTokensImmediately()
    {
        var tokens = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());

        using (var logout = await _auth.LogoutAsync(tokens.AccessToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        using var me = await _auth.MeAsync(tokens.AccessToken);
        using var refresh = await _auth.RefreshAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesTheRefreshToken()
    {
        var tokens = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());

        using var refresh = await _auth.RefreshAsync(tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var renewed = await AuthTestClient.ReadTokensAsync(refresh);

        Assert.NotEqual(tokens.RefreshToken, renewed.RefreshToken);
        Assert.NotEqual(tokens.AccessToken, renewed.AccessToken);
        // A sessão tem expiração absoluta: a renovação não a estende.
        Assert.Equal(tokens.RefreshTokenExpiresAt, renewed.RefreshTokenExpiresAt);

        using var me = await _auth.MeAsync(renewed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task RefreshTokenReuse_RevokesTheWholeSession()
    {
        var tokens = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());
        using var firstRefresh = await _auth.RefreshAsync(tokens.RefreshToken);
        var renewed = await AuthTestClient.ReadTokensAsync(firstRefresh);

        // Reapresentar o token já trocado (cenário de roubo) revoga a sessão inteira.
        using (var reuse = await _auth.RefreshAsync(tokens.RefreshToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        }

        using var legitimateRefresh = await _auth.RefreshAsync(renewed.RefreshToken);
        using var me = await _auth.MeAsync(renewed.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, legitimateRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task ExpiredAccessToken_IsRejected()
    {
        var tokens = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());
        var issuedAt = DateTime.UtcNow.AddHours(-2);
        var expired = CreateToken(tokens.AccessToken, _factory.SigningKey, issuedAt, issuedAt.AddMinutes(15));

        using var me = await _auth.MeAsync(expired);

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task AccessTokenSignedWithAnotherKey_IsRejected()
    {
        var tokens = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());
        var forged = CreateToken(tokens.AccessToken, "chave-de-um-atacante-com-tamanho-suficiente-123", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(15));

        using var me = await _auth.MeAsync(forged);

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task TamperedAccessToken_IsRejected()
    {
        var tokens = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());
        var parts = tokens.AccessToken.Split('.');
        var payload = parts[1];
        var tampered = $"{parts[0]}.{(payload[0] == 'a' ? 'b' : 'a')}{payload[1..]}.{parts[2]}";

        using var me = await _auth.MeAsync(tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    /// <summary>Reemite um token com as mesmas claims (usuário e sessão reais), mas outra chave ou validade.</summary>
    private static string CreateToken(string originalToken, string signingKey, DateTime issuedAt, DateTime expires)
    {
        var original = new JsonWebToken(originalToken);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = original.Issuer,
            Audience = original.Audiences.Single(),
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expires,
            Claims = new Dictionary<string, object>
            {
                [AuthClaimTypes.Subject] = original.GetClaim(AuthClaimTypes.Subject).Value,
                [AuthClaimTypes.SessionId] = original.GetClaim(AuthClaimTypes.SessionId).Value,
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
