using EasyStock.Application.UseCases.ResetarSenha;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class ResetarSenhaCommandValidator : AbstractValidator<ResetarSenhaCommand>
{
    public ResetarSenhaCommandValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token e obrigatorio.");

        RuleFor(x => x.NovaSenha)
            .NotEmpty().WithMessage("Nova senha e obrigatoria.")
            .MinimumLength(8).WithMessage("Senha deve ter pelo menos 8 caracteres.")
            .Matches(@"[A-Z]").WithMessage("Senha deve conter pelo menos uma letra maiuscula.")
            .Matches(@"[a-z]").WithMessage("Senha deve conter pelo menos uma letra minuscula.")
            .Matches(@"[0-9]").WithMessage("Senha deve conter pelo menos um numero.")
            .Matches(@"[\W]").WithMessage("Senha deve conter pelo menos um caractere especial.");
    }
}