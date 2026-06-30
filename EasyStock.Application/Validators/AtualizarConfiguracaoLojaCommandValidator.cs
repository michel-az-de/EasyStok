using EasyStock.Application.UseCases.ConfiguracoesLoja;
using FluentValidation;

namespace EasyStock.Application.Validators;

/// <summary>
/// Valida a atualização de configuração da loja. R6 (ADR-0039): impede salvar nível
/// crítico maior ou igual ao mínimo quando AMBOS são informados no comando. O caso de
/// patch parcial (só um informado) é barrado no use case, que conhece o estado persistido.
/// </summary>
public class AtualizarConfiguracaoLojaCommandValidator : AbstractValidator<AtualizarConfiguracaoLojaCommand>
{
    public AtualizarConfiguracaoLojaCommandValidator()
    {
        RuleFor(x => x.EmpresaId)
            .NotEmpty().WithMessage("EmpresaId é obrigatório.");

        RuleFor(x => x.LojaId)
            .NotEmpty().WithMessage("LojaId é obrigatório.");

        RuleFor(x => x.QuantidadeMinimaPadrao)
            .GreaterThanOrEqualTo(0).When(x => x.QuantidadeMinimaPadrao.HasValue)
            .WithMessage("O mínimo de estoque não pode ser negativo.");

        RuleFor(x => x.QuantidadeCriticaPadrao)
            .GreaterThanOrEqualTo(0).When(x => x.QuantidadeCriticaPadrao.HasValue)
            .WithMessage("O nível crítico não pode ser negativo.");

        // R6: crítico deve ser estritamente menor que o mínimo quando ambos vêm no comando.
        RuleFor(x => x.QuantidadeCriticaPadrao)
            .Must((cmd, critica) => critica!.Value < cmd.QuantidadeMinimaPadrao!.Value)
            .When(x => x.QuantidadeCriticaPadrao.HasValue && x.QuantidadeMinimaPadrao.HasValue)
            .WithMessage("O nível crítico deve ser menor que o mínimo de estoque.");
    }
}
