using System.ComponentModel.DataAnnotations;

namespace ProjectManager.Application.Common;

/// <summary>
/// Campo de texto opcional que, quando enviado, precisa ter conteúdo: recusa vazio e texto só com
/// espaços (inclusive tabulação, quebra de linha e espaço não separável). É o equivalente de
/// <see cref="RequiredAttribute"/> para os campos de um PATCH, em que a ausência mantém o valor atual.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NotWhiteSpaceAttribute : ValidationAttribute
{
    public NotWhiteSpaceAttribute()
        : base("O campo {0} não pode ficar em branco.")
    {
    }

    public override bool IsValid(object? value) => value is not string text || !string.IsNullOrWhiteSpace(text);
}
