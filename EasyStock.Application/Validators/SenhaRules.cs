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
        rule.NotEmpty().WithMessage("Escolha uma senha.")
            .MinimumLength(TamanhoMinimo).WithMessage($"A senha precisa ter {TamanhoMinimo} caracteres ou mais.")
            .Matches("[A-Z]").WithMessage("A senha precisa ter uma letra maiúscula.")
            .Matches("[a-z]").WithMessage("A senha precisa ter uma letra minúscula.")
            .Matches("[0-9]").WithMessage("A senha precisa ter um número.")
            .Matches("[\\W]").WithMessage("A senha precisa ter um símbolo, como ! ou @.");
}
