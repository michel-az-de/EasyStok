using EasyStock.Application.UseCases.CadastrarProduto;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class CadastrarProdutoCommandValidator : AbstractValidator<CadastrarProdutoCommand>
{
    public CadastrarProdutoCommandValidator()
    {
        RuleFor(x => x.EmpresaId)
            .NotEmpty().WithMessage("EmpresaId é obrigatório.");

        RuleFor(x => x.CategoriaId)
            .NotEmpty().WithMessage("CategoriaId é obrigatório.");

        RuleFor(x => x.Nome)
            .NotEmpty().WithMessage("Nome é obrigatório.")
            .MinimumLength(3).WithMessage("Nome deve ter pelo menos 3 caracteres.")
            .MaximumLength(200).WithMessage("Nome deve ter no máximo 200 caracteres.");

        RuleFor(x => x.Tipo)
            .IsInEnum().WithMessage("Tipo deve ser um valor válido.");

        RuleFor(x => x.PrecoReferencia)
            .GreaterThan(0).When(x => x.PrecoReferencia.HasValue).WithMessage("Preço de referência deve ser maior que zero.")
            .LessThanOrEqualTo(LimitesProduto.ValorMaximo).When(x => x.PrecoReferencia.HasValue).WithMessage("Preço de referência deve ser no máximo R$ 99.999.999,99.");

        // BUG-04: piso (nao-negativo) que faltava + teto de sanidade anti-typo (igual ao preco).
        RuleFor(x => x.CustoReferencia)
            .GreaterThanOrEqualTo(0).When(x => x.CustoReferencia.HasValue).WithMessage("Custo de referência não pode ser negativo.")
            .LessThanOrEqualTo(LimitesProduto.ValorMaximo).When(x => x.CustoReferencia.HasValue).WithMessage("Custo de referência deve ser no máximo R$ 99.999.999,99.");
    }
}