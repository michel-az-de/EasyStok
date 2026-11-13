using EasyStock.Application.Validators;
using EasyStock.Domain.ValueObjects;
using FluentValidation;

namespace EasyStock.Application.Tests.Validators;

/// <summary>
/// #2018 (estrutura-21): a política de senha tinha duas "fontes únicas": <c>SenhaRules</c> (viva nos validators, sem teto) e
/// <c>PasswordPolicy</c> (só nos testes, com teto). Agora os validators aplicam <see cref="PasswordPolicy"/>, com as mesmas
/// mensagens de antes por critério.
/// </summary>
public class PoliticaDeSenhaTests
{
    private sealed record Comando(string Senha);

    private sealed class Validador : AbstractValidator<Comando>
    {
        public Validador() => RuleFor(x => x.Senha).AplicarPoliticaSenha();
    }

    private static IReadOnlyList<string> Erros(string senha) =>
        new Validador().Validate(new Comando(senha)).Errors.Select(e => e.ErrorMessage).ToList();

    // ── O que a regra antiga e a nova concordam ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Senha@12345")]
    [InlineData("MinhaSenha#2026")]
    [InlineData("Abcdefgh1!")]
    public void SenhaForte_Passa(string senha) => Erros(senha).Should().BeEmpty();

    [Theory]
    [InlineData("Aa1!aaaa", "A senha precisa ter 10 caracteres ou mais.")]
    [InlineData("senha@12345", "A senha precisa ter uma letra maiúscula.")]
    [InlineData("SENHA@12345", "A senha precisa ter uma letra minúscula.")]
    [InlineData("Senha@abcdef", "A senha precisa ter um número.")]
    [InlineData("Senha123456", "A senha precisa ter um símbolo, como ! ou @.")]
    public void CadaCriterioTemASuaMensagem(string senha, string mensagem) => Erros(senha).Should().Contain(mensagem);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SenhaVazia_PedeParaEscolher(string senha) => Erros(senha).Should().Contain("Escolha uma senha.");

    // ── Onde as duas divergiam: vale a de PasswordPolicy (Domain) ────────────────────────────────────────

    [Fact]
    public void SenhaGigante_RecusadaComOTetoDaPolitica()
    {
        var gigante = "Aa1!" + new string('x', PasswordPolicy.MaxLength);

        Erros(gigante).Should().Contain($"A senha pode ter no máximo {PasswordPolicy.MaxLength} caracteres.");
    }

    [Fact]
    public void SenhaNoTeto_Passa() =>
        Erros("Aa1!" + new string('x', PasswordPolicy.MaxLength - 4)).Should().BeEmpty();

    [Fact]
    public void SublinhadoContaComoSimbolo_ComoNaPoliticaDoDominio() =>
        Erros("Abcdefgh1_").Should().BeEmpty();

    [Fact]
    public void LetraAcentuadaContaComoMaiusculaEMinuscula_ComoNaPoliticaDoDominio() =>
        Erros("ÇÃÕçãõççç1!").Should().BeEmpty();

    [Theory]
    [InlineData("Senha@12345", true)]
    [InlineData("Senha123456", false)]
    [InlineData("Aa1!aaaa", false)]
    [InlineData("Abcdefgh1_", true)]
    public void ValidadorEPolicyConcordam(string senha, bool valida)
    {
        PasswordPolicy.IsValid(senha).Should().Be(valida);
        (Erros(senha).Count == 0).Should().Be(valida);
    }
}
