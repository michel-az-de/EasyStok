using EasyStock.Application.UseCases.ResetarSenha;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class ResetarSenhaPorCodigoCommandValidator : AbstractValidator<ResetarSenhaPorCodigoCommand>
{
    public ResetarSenhaPorCodigoCommandValidator()
    {
        // O formato do código fica no use case: formato ruim recebe a mesma recusa genérica do código errado.
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email é obrigatório.");

        RuleFor(x => x.NovaSenha)
            .AplicarPoliticaSenha();
    }
}
