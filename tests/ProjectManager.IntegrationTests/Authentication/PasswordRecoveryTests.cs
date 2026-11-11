using System.Net;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Authentication;

/// <summary>
/// Recuperação de senha com e-mail real (Mailpit).
/// </summary>
public sealed class PasswordRecoveryTests : IDisposable
{
    private const string NewPassword = "uma-nova-senha-muito-segura";

    private readonly MailpitFixture _mailpit;
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly AuthTestClient _auth;

    public PasswordRecoveryTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _mailpit = mailpit;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _auth = new AuthTestClient(_client, mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResetPassword_ChangesPasswordAndRevokesExistingSessions()
    {
        var email = AuthTestClient.NewEmail();
        var sessionBeforeReset = await _auth.CreateConfirmedUserAndLoginAsync(email);

        using (var forgot = await _auth.ForgotPasswordAsync(email))
        {
            Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        }

        var message = await _mailpit.WaitForEmailAsync(email, AuthTestClient.ResetSubject, CancellationToken);
        using (var reset = await _auth.ResetPasswordAsync(email, message.LinkParameter("token"), NewPassword))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }

        using var oldPassword = await _auth.LoginAsync(email, AuthTestClient.Password);
        using var oldSession = await _auth.MeAsync(sessionBeforeReset.AccessToken);
        using var newPassword = await _auth.LoginAsync(email, NewPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task ResetToken_CannotBeUsedTwice()
    {
        var email = AuthTestClient.NewEmail();
        await _auth.CreateConfirmedUserAndLoginAsync(email);
        using (var forgot = await _auth.ForgotPasswordAsync(email))
        {
            forgot.EnsureSuccessStatusCode();
        }

        var token = (await _mailpit.WaitForEmailAsync(email, AuthTestClient.ResetSubject, CancellationToken)).LinkParameter("token");
        using (var first = await _auth.ResetPasswordAsync(email, token, NewPassword))
        {
            first.EnsureSuccessStatusCode();
        }

        using var second = await _auth.ResetPasswordAsync(email, token, "mais-uma-senha-diferente");

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_Returns202AndSendsNothing()
    {
        var email = AuthTestClient.NewEmail();

        using var response = await _auth.ForgotPasswordAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        Assert.Equal(0, await _mailpit.CountEmailsAsync(email, CancellationToken));
    }

    [Fact]
    public async Task ResetPassword_WithInvalidToken_ReturnsBadRequest()
    {
        var email = AuthTestClient.NewEmail();
        await _auth.CreateConfirmedUserAndLoginAsync(email);

        using var response = await _auth.ResetPasswordAsync(email, "dG9rZW4taW52YWxpZG8", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
