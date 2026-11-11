using Microsoft.Extensions.Options;
using ProjectManager.Application.Abstractions.Email;
using ProjectManager.Infrastructure.Identity;

namespace ProjectManager.Infrastructure.Organizations;

internal sealed class OrganizationEmails(IOptions<AccountLinkOptions> options)
{
    public EmailMessage Invitation(string to, string organizationName, string inviterName, string roleName, string token)
    {
        var baseUrl = options.Value.WebBaseUrl
            ?? throw new InvalidOperationException($"{AccountLinkOptions.SectionName}:WebBaseUrl não configurado.");
        var link = new Uri(baseUrl, $"convites/aceitar?token={Uri.EscapeDataString(token)}");

        return new EmailMessage(
            to,
            $"Convite para {organizationName} no Gestalyn",
            $"""
            {inviterName} convidou você para participar da organização "{organizationName}" no Gestalyn, com o papel {roleName}.

            Para aceitar, abra o link abaixo e entre com uma conta que use este e-mail ({to}).
            Se ainda não tem conta, crie uma com este mesmo e-mail e depois abra o link novamente.

            {link}

            O convite expira em 7 dias. Se não esperava este convite, ignore esta mensagem.
            """);
    }
}
