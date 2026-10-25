using EasyStock.Domain.ValueObjects;
using FluentValidation;

namespace EasyStock.Application.Validators;

/// <summary>
/// Aplica a política de senha do SaaS (#767) nos validators. A política é uma só, a do Domain
/// (<see cref="PasswordPolicy"/>, #2018): antes esta classe tinha a sua própria regra, sem teto de tamanho, e divergia da do
/// Domain em quem conta como maiúscula, minúscula e símbolo. Aqui só ficam as mensagens de cada critério.
/// </summary>
public static class SenhaRules
{
    public const int TamanhoMinimo = PasswordPolicy.MinLength;

    private static string Mensagem(FalhaDeSenha falha) => falha switch
    {
        FalhaDeSenha.Vazia => "Escolha uma senha.",
        FalhaDeSenha.Curta => $"A senha precisa ter {PasswordPolicy.MinLength} caracteres ou mais.",
        FalhaDeSenha.Longa => $"A senha pode ter no máximo {PasswordPolicy.MaxLength} caracteres.",
        FalhaDeSenha.SemMaiuscula => "A senha precisa ter uma letra maiúscula.",
        FalhaDeSenha.SemMinuscula => "A senha precisa ter uma letra minúscula.",
        FalhaDeSenha.SemNumero => "A senha precisa ter um número.",
        FalhaDeSenha.SemSimbolo => "A senha precisa ter um símbolo, como ! ou @.",
        _ => "Senha inválida.",
    };

    /// <summary>Aplica a política canônica de senha forte (<see cref="PasswordPolicy"/>) a uma propriedade string.</summary>
    public static IRuleBuilderOptions<T, string> AplicarPoliticaSenha<T>(this IRuleBuilder<T, string> rule)
    {
        var regra = rule.Must(senha => Cumpre(senha, FalhaDeSenha.Vazia)).WithMessage(Mensagem(FalhaDeSenha.Vazia));
        foreach (var falha in new[]
        {
            FalhaDeSenha.Curta, FalhaDeSenha.Longa, FalhaDeSenha.SemMaiuscula, FalhaDeSenha.SemMinuscula,
            FalhaDeSenha.SemNumero, FalhaDeSenha.SemSimbolo,
        })
            regra = regra.Must(senha => Cumpre(senha, falha)).WithMessage(Mensagem(falha));
        return regra;
    }

    private static bool Cumpre(string? senha, FalhaDeSenha criterio) => !PasswordPolicy.Falhas(senha).Contains(criterio);
}
