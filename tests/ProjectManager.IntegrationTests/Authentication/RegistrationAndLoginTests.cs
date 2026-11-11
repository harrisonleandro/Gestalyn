using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Authentication;

public sealed class RegistrationAndLoginTests : IDisposable
{
    private readonly PostgresDatabaseFixture _database;
    private readonly MailpitFixture _mailpit;
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly AuthTestClient _auth;

    public RegistrationAndLoginTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _mailpit = mailpit;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _auth = new AuthTestClient(_client, mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_ConfirmEmail_Login_Me_CompletesTheFlow()
    {
        var email = AuthTestClient.NewEmail();

        using var registration = await _auth.RegisterAsync(email, displayName: "Ana Teste");
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);

        using (var beforeConfirmation = await _auth.LoginAsync(email))
        {
            Assert.Equal(HttpStatusCode.Forbidden, beforeConfirmation.StatusCode);
            Assert.Equal("application/problem+json", beforeConfirmation.Content.Headers.ContentType?.MediaType);
            var problem = await beforeConfirmation.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken);
            Assert.Contains("Confirme o e-mail", problem!.Detail, StringComparison.Ordinal);
        }

        await _auth.ConfirmEmailFromMailAsync(email);
        var tokens = await _auth.LoginTokensAsync(email);

        using var me = await _auth.MeAsync(tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var profile = await me.Content.ReadFromJsonAsync<UserProfile>(CancellationToken);
        Assert.Equal(email, profile!.Email);
        Assert.Equal("Ana Teste", profile.DisplayName);
        Assert.True(profile.EmailConfirmed);
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.True(tokens.RefreshTokenExpiresAt > tokens.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task Register_StoresOnlyThePasswordHash()
    {
        var email = AuthTestClient.NewEmail();
        using var registration = await _auth.RegisterAsync(email);
        registration.EnsureSuccessStatusCode();

        await using var context = _database.CreateContext(organizationId: null);
        var user = await context.Users.AsNoTracking().SingleAsync(u => u.Email == email, CancellationToken);

        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.DoesNotContain(AuthTestClient.Password, user.PasswordHash, StringComparison.Ordinal);
        Assert.Equal(7, user.Id.Version);
    }

    [Fact]
    public async Task Register_ExistingEmail_ReturnsSameResponseAndNotifiesOwner()
    {
        var email = AuthTestClient.NewEmail();
        using (var first = await _auth.RegisterAsync(email))
        {
            first.EnsureSuccessStatusCode();
        }

        using var second = await _auth.RegisterAsync(email, password: "outra-senha-bem-longa");

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var notice = await _mailpit.WaitForEmailAsync(email, "já possui uma conta", CancellationToken);
        Assert.Contains("já está cadastrado", notice.TextBody, StringComparison.Ordinal);

        await using var context = _database.CreateContext(organizationId: null);
        Assert.Equal(1, await context.Users.CountAsync(u => u.Email == email, CancellationToken));
    }

    [Fact]
    public async Task Register_ShortPassword_ReturnsValidationProblem()
    {
        using var response = await _auth.RegisterAsync(AuthTestClient.NewEmail(), password: "curta");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken);
        Assert.True(problem!.Errors.ContainsKey("Password"));
    }

    [Fact]
    public async Task ConfirmEmail_WithInvalidToken_ReturnsBadRequest()
    {
        using var response = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/confirm-email", UriKind.Relative),
            new { userId = Guid.CreateVersion7(), token = "token-invalido" },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        using var response = await _auth.MeAsync(accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
