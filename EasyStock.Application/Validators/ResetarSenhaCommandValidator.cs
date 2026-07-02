using EasyStock.Application.UseCases.ResetarSenha;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class ResetarSenhaCommandValidator : AbstractValidator<ResetarSenhaCommand>
{
    public ResetarSenhaCommandValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token é obrigatório.");

        RuleFor(x => x.NovaSenha)
            .AplicarPoliticaSenha();
    }
}