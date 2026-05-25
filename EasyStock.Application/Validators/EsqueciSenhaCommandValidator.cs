using EasyStock.Application.UseCases.EsqueciSenha;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class EsqueciSenhaCommandValidator : AbstractValidator<EsqueciSenhaCommand>
{
    public EsqueciSenhaCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email é obrigatório.")
            .EmailAddress().WithMessage("Email deve ser valido.");
    }
}
