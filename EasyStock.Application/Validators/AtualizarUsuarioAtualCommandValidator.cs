using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class AtualizarUsuarioAtualCommandValidator : AbstractValidator<AtualizarUsuarioAtualCommand>
{
    public AtualizarUsuarioAtualCommandValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty().WithMessage("Nome é obrigatório.")
            .MaximumLength(150).WithMessage("Nome deve ter no maximo 150 caracteres.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email é obrigatório.")
            .EmailAddress().WithMessage("Email deve ser valido.")
            .MaximumLength(255).WithMessage("Email deve ter no maximo 255 caracteres.");
    }
}