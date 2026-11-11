using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Authentication;

/// <summary>
/// Proteções contra abuso e vazamento: respostas neutras, bloqueio de conta, rate limiting e logs.
/// </summary>
public sealed class AuthSecurityTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_UnknownEmailAndWrongPassword_ReturnIdenticalResponses()
    {
        using var factory = new ApiFactory(database.ConnectionString, mailpit);
        using var client = factory.CreateClient();
        var auth = new AuthTestClient(client, mailpit);
        var email = AuthTestClient.NewEmail();
        await auth.CreateConfirmedUserAndLoginAsync(email);

        using var wrongPassword = await auth.LoginAsync(email, "senha-errada-porem-longa");
        using var unknownEmail = await auth.LoginAsync(AuthTestClient.NewEmail(), "senha-errada-porem-longa");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await ProblemTextAsync(wrongPassword), await ProblemTextAsync(unknownEmail));
    }

    [Fact]
    public async Task Lockout_AfterFiveFailures_BlocksEvenTheCorrectPassword()
    {
        using var factory = new ApiFactory(database.ConnectionString, mailpit);
        using var client = factory.CreateClient();
        var auth = new AuthTestClient(client, mailpit);
        var email = AuthTestClient.NewEmail();
        await auth.CreateConfirmedUserAndLoginAsync(email);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await auth.LoginAsync(email, "senha-errada-porem-longa");
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var correctPassword = await auth.LoginAsync(email);
        Assert.Equal(HttpStatusCode.Unauthorized, correctPassword.StatusCode);

        await using var context = database.CreateContext(organizationId: null);
        var user = await context.Users.AsNoTracking().SingleAsync(u => u.Email == email, CancellationToken);
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task AuthEndpoints_AreRateLimitedPerClient()
    {
        using var factory = new ApiFactory(
            database.ConnectionString,
            mailpit,
            new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "3", ["RateLimiting:Auth:WindowSeconds"] = "60" });
        using var client = factory.CreateClient();
        var auth = new AuthTestClient(client, mailpit);

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var response = await auth.LoginAsync(AuthTestClient.NewEmail(), "senha-errada-porem-longa");
            statuses.Add(response.StatusCode);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            }
        }

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests],
            statuses);
    }

    [Fact]
    public async Task Logs_NeverContainPasswordsOrTokens()
    {
        using var factory = new ApiFactory(database.ConnectionString, mailpit);
        using var client = factory.CreateClient();
        var auth = new AuthTestClient(client, mailpit);
        var email = AuthTestClient.NewEmail();

        var tokens = await auth.CreateConfirmedUserAndLoginAsync(email);
        using (var failure = await auth.LoginAsync(email, "senha-errada-porem-longa"))
        using (var refresh = await auth.RefreshAsync(tokens.RefreshToken))
        using (var logout = await auth.LogoutAsync(tokens.AccessToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        var secrets = new[] { AuthTestClient.Password, "senha-errada-porem-longa", tokens.AccessToken, tokens.RefreshToken };
        Assert.NotEmpty(factory.Logs.Entries);
        Assert.All(factory.Logs.Entries, entry =>
            Assert.DoesNotContain(secrets, secret => entry.Contains(secret, StringComparison.Ordinal)));
    }

    /// <summary>Corpo da resposta sem o traceId, que é diferente a cada requisição.</summary>
    private static async Task<string> ProblemTextAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        return Regex.Replace(body, "\"traceId\":\"[^\"]*\"", string.Empty);
    }
}
