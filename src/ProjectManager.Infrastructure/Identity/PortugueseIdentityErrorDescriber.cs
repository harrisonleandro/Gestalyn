using Microsoft.AspNetCore.Identity;

namespace ProjectManager.Infrastructure.Identity;

/// <summary>
/// Mensagens do Identity exibidas ao usuário, em português. Cobre os erros alcançáveis com a política
/// configurada (tamanho mínimo de senha, e-mail inválido, token inválido).
/// </summary>
internal sealed class PortugueseIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() =>
        new() { Code = nameof(DefaultError), Description = "Não foi possível concluir a operação." };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"A senha deve ter pelo menos {length} caracteres." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"A senha deve ter pelo menos {uniqueChars} caracteres diferentes." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "Informe um e-mail válido." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = "Informe um e-mail válido." };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = "O link é inválido ou expirou." };
}
