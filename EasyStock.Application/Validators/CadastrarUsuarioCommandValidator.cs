using EasyStock.Application.UseCases.CadastrarUsuario;
using FluentValidation;

namespace EasyStock.Application.Validators;

public class CadastrarUsuarioCommandValidator : AbstractValidator<CadastrarUsuarioCommand>
{
    public CadastrarUsuarioCommandValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty().WithMessage("Nome é obrigatório.")
            .MaximumLength(150).WithMessage("Nome deve ter no maximo 150 caracteres.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email é obrigatório.")
            .EmailAddress().WithMessage("Email deve ser valido.")
            .MaximumLength(255).WithMessage("Email deve ter no maximo 255 caracteres.");

        RuleFor(x => x.Senha)
            .AplicarPoliticaSenha();
    }
}