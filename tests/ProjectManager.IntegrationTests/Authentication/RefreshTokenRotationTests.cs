using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Infrastructure.Identity;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Authentication;

/// <summary>
/// Rotação idempotente do refresh token (ADR-012), com relógio controlado: requisições simultâneas
/// legítimas recebem o mesmo sucessor, e qualquer divergência da cadeia (o cenário em que um invasor usa
/// o token primeiro) revoga a sessão em vez de desconectar só a vítima.
/// </summary>
public sealed class RefreshTokenRotationTests : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly PostgresDatabaseFixture _database;
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly AuthTestClient _auth;

    public RefreshTokenRotationTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit, timeProvider: _clock);
        _client = _factory.CreateClient();
        _auth = new AuthTestClient(_client, mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ParallelRefreshes_WithTheSameToken_AllReceiveTheSameSuccessor()
    {
        var login = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());

        // Uma rotação e cinco repetições: exatamente o limite padrão.
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _auth.RefreshAsync(login.RefreshToken)));
        var results = new List<AuthTokens>();
        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                results.Add(await AuthTestClient.ReadTokensAsync(response));
            }
        }

        var successor = Assert.Single(results.Select(tokens => tokens.RefreshToken).Distinct());
        using var next = await _auth.RefreshAsync(successor);
        using var me = await _auth.MeAsync(results[0].AccessToken);

        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(3, await TokensOfSessionAsync(me));
    }

    [Fact]
    public async Task AttackerFirst_VictimInWindow_StaysConnected_UntilTheChainDivergesAndTheSessionIsRevoked()
    {
        var login = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());

        // O invasor usa o token roubado primeiro.
        var attacker = await RefreshOkAsync(login.RefreshToken);

        // A vítima chega 5 s depois com o mesmo token: recebe o mesmo sucessor e continua conectada.
        _clock.UtcNow += TimeSpan.FromSeconds(5);
        var victim = await RefreshOkAsync(login.RefreshToken);
        Assert.Equal(attacker.RefreshToken, victim.RefreshToken);

        // O invasor renova de novo; a vítima, depois da janela, apresenta o sucessor já trocado.
        _clock.UtcNow += TimeSpan.FromSeconds(1);
        var attackerNext = await RefreshOkAsync(attacker.RefreshToken);
        _clock.UtcNow += Window + TimeSpan.FromSeconds(1);
        using var victimLate = await _auth.RefreshAsync(victim.RefreshToken);

        // A divergência revoga a sessão: o invasor perde o acesso.
        using var attackerRefresh = await _auth.RefreshAsync(attackerNext.RefreshToken);
        using var attackerMe = await _auth.MeAsync(attackerNext.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, victimLate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, attackerRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, attackerMe.StatusCode);
        Assert.Equal(SessionRevocationReason.RefreshTokenReuse, await RevocationReasonAsync(login));
        Assert.DoesNotContain(_factory.Logs.Entries, entry =>
            entry.Contains(login.RefreshToken, StringComparison.Ordinal) || entry.Contains(attacker.RefreshToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AttackerAdvancesTheChain_VictimInWindow_RevokesTheSessionImmediately()
    {
        var login = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());

        var first = await RefreshOkAsync(login.RefreshToken);
        var second = await RefreshOkAsync(first.RefreshToken);

        // Dentro da janela, mas o sucessor já foi usado: não é concorrência, é uma cadeia paralela.
        _clock.UtcNow += TimeSpan.FromSeconds(2);
        using var victim = await _auth.RefreshAsync(login.RefreshToken);
        using var attackerRefresh = await _auth.RefreshAsync(second.RefreshToken);
        using var attackerMe = await _auth.MeAsync(second.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, victim.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, attackerRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, attackerMe.StatusCode);
    }

    [Fact]
    public async Task ReuseAfterTheWindow_RevokesTheSession()
    {
        var login = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());
        var renewed = await RefreshOkAsync(login.RefreshToken);

        _clock.UtcNow += Window + TimeSpan.FromSeconds(1);
        using var reuse = await _auth.RefreshAsync(login.RefreshToken);
        using var legitimate = await _auth.RefreshAsync(renewed.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, legitimate.StatusCode);
    }

    [Fact]
    public async Task ReplaysBeyondTheLimit_RevokeTheSession()
    {
        var login = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());
        var renewed = await RefreshOkAsync(login.RefreshToken);

        // Limite padrão: 5 repetições do mesmo token.
        for (var replay = 0; replay < 5; replay++)
        {
            Assert.Equal(renewed.RefreshToken, (await RefreshOkAsync(login.RefreshToken)).RefreshToken);
        }

        using var excess = await _auth.RefreshAsync(login.RefreshToken);
        using var afterExcess = await _auth.RefreshAsync(renewed.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, excess.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterExcess.StatusCode);
    }

    [Fact]
    public async Task ClosedSession_IsNotReopenedByAReplay()
    {
        var login = await _auth.CreateConfirmedUserAndLoginAsync(AuthTestClient.NewEmail());
        var renewed = await RefreshOkAsync(login.RefreshToken);

        using (var logout = await _auth.LogoutAsync(renewed.AccessToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        using var replay = await _auth.RefreshAsync(login.RefreshToken);
        using var successor = await _auth.RefreshAsync(renewed.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);
        Assert.Equal(SessionRevocationReason.Logout, await RevocationReasonAsync(login));
    }

    private async Task<AuthTokens> RefreshOkAsync(string refreshToken)
    {
        using var response = await _auth.RefreshAsync(refreshToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await AuthTestClient.ReadTokensAsync(response);
    }

    private async Task<int> TokensOfSessionAsync(HttpResponseMessage me)
    {
        var profile = await me.Content.ReadFromJsonAsync<UserProfile>(CancellationToken);
        await using var context = _database.CreateContext(organizationId: null);

        return await context.UserRefreshTokens.CountAsync(token => token.Session.UserId == profile!.Id, CancellationToken);
    }

    private async Task<SessionRevocationReason?> RevocationReasonAsync(AuthTokens login)
    {
        // A sessão vem da claim sid do access token emitido no login.
        var sessionId = Guid.Parse(new JsonWebToken(login.AccessToken).GetClaim("sid").Value);
        await using var context = _database.CreateContext(organizationId: null);

        return await context.UserSessions.Where(s => s.Id == sessionId).Select(s => s.RevocationReason).SingleAsync(CancellationToken);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
