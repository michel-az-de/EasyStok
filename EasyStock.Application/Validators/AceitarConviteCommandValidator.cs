using EasyStock.Application.UseCases.AceitarConvite;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class AceitarConviteCommandValidator : AbstractValidator<AceitarConviteCommand>
{
    public AceitarConviteCommandValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Convite é obrigatório.");

        RuleFor(x => x.NovaSenha)
            .AplicarPoliticaSenha();
    }
}
