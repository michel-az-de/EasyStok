using FluentValidation;

namespace EasyStock.Application.Validators;

/// <summary>
/// Fonte única da política de senha do SaaS (#767). Antes divergia entre fluxos:
/// AlterarSenha exigia 8 caracteres, Cadastrar/Resetar exigiam 10, e o caminho
/// PUT /usuarios/{id}/senha não validava nada. Unificado no mínimo mais forte (10)
/// + complexidade (maiúscula, minúscula, número, caractere especial).
/// </summary>
public static class SenhaRules
{
    public const int TamanhoMinimo = 10;

    /// <summary>
    /// Aplica a política canônica de senha forte a uma propriedade string.
    /// </summary>
    public static IRuleBuilderOptions<T, string> AplicarPoliticaSenha<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Nova senha e obrigatoria.")
            .MinimumLength(TamanhoMinimo).WithMessage($"Senha deve ter pelo menos {TamanhoMinimo} caracteres.")
            .Matches("[A-Z]").WithMessage("Senha deve conter pelo menos uma letra maiuscula.")
            .Matches("[a-z]").WithMessage("Senha deve conter pelo menos uma letra minuscula.")
            .Matches("[0-9]").WithMessage("Senha deve conter pelo menos um numero.")
            .Matches("[\\W]").WithMessage("Senha deve conter pelo menos um caractere especial.");
}
