using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Authentication;

/// <summary>
/// Chamadas aos endpoints <c>/api/v1/auth</c> usadas pelos testes, incluindo a leitura dos links
/// recebidos por e-mail no Mailpit.
/// </summary>
internal sealed class AuthTestClient(HttpClient client, MailpitFixture mailpit)
{
    public const string Password = "senha-de-teste-segura-123";

    public const string ConfirmationSubject = "Confirme seu e-mail";

    public const string ResetSubject = "Redefinição de senha";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static string NewEmail() => $"{Guid.NewGuid():N}@teste.gestalyn";

    public Task<HttpResponseMessage> RegisterAsync(string email, string password = Password, string displayName = "Pessoa de Teste") =>
        PostAsync("register", new { email, password, displayName });

    public Task<HttpResponseMessage> LoginAsync(string email, string password = Password) =>
        PostAsync("login", new { email, password });

    public Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        PostAsync("refresh", new { refreshToken });

    public Task<HttpResponseMessage> ForgotPasswordAsync(string email) =>
        PostAsync("forgot-password", new { email });

    public Task<HttpResponseMessage> ResetPasswordAsync(string email, string token, string newPassword) =>
        PostAsync("reset-password", new { email, token, newPassword });

    public Task<HttpResponseMessage> MeAsync(string? accessToken) => SendAsync(HttpMethod.Get, "me", accessToken);

    public Task<HttpResponseMessage> LogoutAsync(string? accessToken) => SendAsync(HttpMethod.Post, "logout", accessToken);

    public async Task ConfirmEmailFromMailAsync(string email)
    {
        var message = await mailpit.WaitForEmailAsync(email, ConfirmationSubject, CancellationToken);
        using var response = await PostAsync("confirm-email", new
        {
            userId = message.LinkParameter("userId"),
            token = message.LinkParameter("token"),
        });

        response.EnsureSuccessStatusCode();
    }

    /// <summary>Cadastra, confirma o e-mail e abre uma sessão.</summary>
    public async Task<AuthTokens> CreateConfirmedUserAndLoginAsync(string email)
    {
        using (var registration = await RegisterAsync(email))
        {
            registration.EnsureSuccessStatusCode();
        }

        await ConfirmEmailFromMailAsync(email);

        return await LoginTokensAsync(email);
    }

    public async Task<AuthTokens> LoginTokensAsync(string email, string password = Password)
    {
        using var response = await LoginAsync(email, password);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthTokens>(CancellationToken))!;
    }

    public static async Task<AuthTokens> ReadTokensAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<AuthTokens>(CancellationToken))!;

    private Task<HttpResponseMessage> PostAsync<T>(string action, T body) =>
        client.PostAsJsonAsync(AuthUri(action), body, CancellationToken);

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string action, string? accessToken)
    {
        var request = new HttpRequestMessage(method, AuthUri(action));
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return SendAndDisposeRequestAsync(request);
    }

    private async Task<HttpResponseMessage> SendAndDisposeRequestAsync(HttpRequestMessage request)
    {
        using (request)
        {
            return await client.SendAsync(request, CancellationToken);
        }
    }

    private static Uri AuthUri(string action) => new($"/api/v1/auth/{action}", UriKind.Relative);
}
