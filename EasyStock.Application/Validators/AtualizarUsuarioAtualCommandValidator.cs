using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class AtualizarUsuarioAtualCommandValidator : AbstractValidator<AtualizarUsuarioAtualCommand>
{
    public AtualizarUsuarioAtualCommandValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty().When(x => x.Nome is not null).WithMessage("Nome é obrigatório.")
            .MaximumLength(150).When(x => x.Nome is not null).WithMessage("Nome deve ter no máximo 150 caracteres.");

        RuleFor(x => x.Email)
            .NotEmpty().When(x => x.Email is not null).WithMessage("Email é obrigatório.")
            .EmailAddress().When(x => x.Email is not null).WithMessage("Email deve ser válido.")
            .MaximumLength(255).When(x => x.Email is not null).WithMessage("Email deve ter no máximo 255 caracteres.");

        RuleFor(x => x.TemaPreferido)
            .Must(value => value is null or "light" or "dark")
            .WithMessage("Tema preferido deve ser 'light' ou 'dark'.");
    }
}
