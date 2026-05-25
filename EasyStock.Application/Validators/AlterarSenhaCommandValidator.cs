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
            .NotEmpty().WithMessage("Nova senha e obrigatoria.")
            .MinimumLength(8).WithMessage("Senha deve ter pelo menos 8 caracteres.")
            .Matches(@"[A-Z]").WithMessage("Senha deve conter pelo menos uma letra maiuscula.")
            .Matches(@"[a-z]").WithMessage("Senha deve conter pelo menos uma letra minuscula.")
            .Matches(@"[0-9]").WithMessage("Senha deve conter pelo menos um numero.")
            .Matches(@"[\W]").WithMessage("Senha deve conter pelo menos um caractere especial.")
            .NotEqual(x => x.SenhaAtual).WithMessage("Nova senha deve ser diferente da atual.");
    }
}
