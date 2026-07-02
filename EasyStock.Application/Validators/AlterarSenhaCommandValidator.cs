using EasyStock.Application.UseCases.AlterarSenha;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class AlterarSenhaCommandValidator : AbstractValidator<AlterarSenhaCommand>
{
    public AlterarSenhaCommandValidator()
    {
        RuleFor(x => x.SenhaAtual)
            .NotEmpty().WithMessage("Senha atual e obrigatoria.");

        RuleFor(x => x.NovaSenha)
            .AplicarPoliticaSenha()
            .NotEqual(x => x.SenhaAtual).WithMessage("Nova senha deve ser diferente da atual.");
    }
}