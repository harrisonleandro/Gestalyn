using System.Buffers.Text;
using System.Text;
using Microsoft.Extensions.Options;
using ProjectManager.Application.Abstractions.Email;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Mensagens de e-mail da conta. Os tokens do Identity são codificados em Base64Url para uso em links.
/// </summary>
internal sealed class AccountEmails(IOptions<AccountLinkOptions> options)
{
    private const string ProductName = "Gestalyn";

    public static string EncodeToken(string token) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));

    public static bool TryDecodeToken(string encodedToken, out string token)
    {
        try
        {
            token = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encodedToken));
            return true;
        }
        catch (FormatException)
        {
            token = string.Empty;
            return false;
        }
    }

    public EmailMessage Confirmation(string to, string displayName, Guid userId, string token)
    {
        var link = BuildLink("conta/confirmar-email", ("userId", userId.ToString()), ("token", EncodeToken(token)));

        return new EmailMessage(
            to,
            $"Confirme seu e-mail no {ProductName}",
            $"""
            Olá, {displayName}.

            Para ativar sua conta no {ProductName}, confirme seu e-mail abrindo o link abaixo:

            {link}

            O link expira em 2 horas. Se você não criou esta conta, ignore esta mensagem.
            """);
    }

    public EmailMessage AlreadyRegistered(string to)
    {
        var loginLink = BuildLink("conta/entrar");
        var resetLink = BuildLink("conta/esqueci-a-senha");

        return new EmailMessage(
            to,
            $"Você já possui uma conta no {ProductName}",
            $"""
            Alguém tentou criar uma conta no {ProductName} com este e-mail, mas ele já está cadastrado.

            Para entrar: {loginLink}
            Se esqueceu a senha: {resetLink}

            Se não foi você, nenhuma ação é necessária.
            """);
    }

    public EmailMessage PasswordReset(string to, string token)
    {
        var link = BuildLink("conta/redefinir-senha", ("email", to), ("token", EncodeToken(token)));

        return new EmailMessage(
            to,
            $"Redefinição de senha do {ProductName}",
            $"""
            Recebemos um pedido para redefinir a senha da sua conta no {ProductName}.

            Para escolher uma nova senha, abra o link abaixo:

            {link}

            O link expira em 2 horas. Ao redefinir a senha, todas as sessões abertas serão encerradas.
            Se você não fez este pedido, ignore esta mensagem; sua senha continua a mesma.
            """);
    }

    private Uri BuildLink(string path, params (string Name, string Value)[] query)
    {
        var baseUrl = options.Value.WebBaseUrl
            ?? throw new InvalidOperationException($"{AccountLinkOptions.SectionName}:WebBaseUrl não configurado.");

        var queryString = string.Join('&', query.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
        var relative = queryString.Length == 0 ? path : $"{path}?{queryString}";

        return new Uri(baseUrl, relative);
    }
}
