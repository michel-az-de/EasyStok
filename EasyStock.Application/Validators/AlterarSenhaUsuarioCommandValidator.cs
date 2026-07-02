using EasyStock.Application.UseCases.AlterarSenhaUsuario;
using FluentValidation;

namespace EasyStock.Application.Validators;

/// <summary>
/// Valida a troca de senha via PUT /api/usuarios/{id}/senha (#767). Este caminho
/// não passava por nenhum validator e aceitava senha fraca, burlando a política do
/// SaaS. Usa a mesma <see cref="SenhaRules.AplicarPoliticaSenha{T}"/> dos demais
/// fluxos de senha (cadastro, reset, alteração do próprio usuário).
/// </summary>
public class AlterarSenhaUsuarioCommandValidator : AbstractValidator<AlterarSenhaCommand>
{
    public AlterarSenhaUsuarioCommandValidator()
    {
        RuleFor(x => x.SenhaAtual)
            .NotEmpty().WithMessage("Senha atual e obrigatoria.");

        RuleFor(x => x.NovaSenha)
            .AplicarPoliticaSenha()
            .NotEqual(x => x.SenhaAtual).WithMessage("Nova senha deve ser diferente da atual.");
    }
}
