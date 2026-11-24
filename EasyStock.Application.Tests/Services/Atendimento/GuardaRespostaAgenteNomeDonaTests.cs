using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#3278 (R4 da revisão #3271): a guarda conhece o nome da dona da loja, não só o da Casa da Baba.</summary>
public class GuardaRespostaAgenteNomeDonaTests
{
    private static readonly string[] Ferramentas = ["consultar_cardapio", "criar_pedido", "escalar_para_dona"];

    private static string? Avaliar(string texto, string? nomeDaDona) =>
        GuardaRespostaAgente.Avaliar(texto, [], Ferramentas, nomeDaDona);

    [Theory]
    [InlineData("Baba", "Faço por aqui sim, a Baba vai combinar com você a entrega e o frete.")]
    [InlineData("Fulana", "A Fulana vai combinar com você a entrega.")]
    [InlineData("Maria Silva", "A Maria Silva vai te chamar para acertar a entrega.")]
    [InlineData("Maria Silva", "A Maria vai te chamar para acertar a entrega.")]
    [InlineData("Júlia", "A JULIA vai ligar para confirmar o horário.")]
    [InlineData("Zé", "O Zé vai entrar em contato com você.")]
    public void PromessaComONomeDaDonaConfiguradaEBarrada(string nome, string texto) =>
        Avaliar(texto, nome).Should().Be("promete encaminhamento sem acionar a equipe");

    [Fact]
    public void SemNomeConfiguradoOsPapeisGenericosContinuamBarrando()
    {
        Avaliar("A dona vai te chamar para acertar a entrega.", null).Should().NotBeNull();
        Avaliar("A equipe vai combinar o frete com você.", "  ").Should().NotBeNull();
    }

    [Fact]
    public void NomeNaoConfiguradoNaoEBarrado()
    {
        // O nome é o da loja. A guarda não adivinha o de outra: só os papéis (dona, equipe, responsável, atendente, ela).
        Avaliar("A Baba vai combinar com você a entrega.", "Fulana").Should().BeNull();
        Avaliar("A Fulana vai combinar com você a entrega.", null).Should().BeNull();
    }

    [Theory]
    [InlineData("A Fulana não vai ligar hoje.")]
    [InlineData("A Fulana não vai combinar nada por fora.")]
    public void NegacaoComONomeDaDonaNaoEPromessa(string texto) => Avaliar(texto, "Fulana").Should().BeNull();

    [Fact]
    public void NomeCurtoNaoCasaDentroDeOutraPalavra()
    {
        Avaliar("A banana vai combinar com o prato do dia.", "Ana").Should().BeNull();
        Avaliar("A Ana vai combinar com você a entrega.", "Ana").Should().NotBeNull();
    }

    [Theory]
    [InlineData("Ju+(a")]
    [InlineData(".*")]
    [InlineData("[Dona")]
    [InlineData("a|b")]
    public void NomeComCaractereDeRegexNaoQuebraENaoCasaTudo(string nome)
    {
        var act = () => Avaliar("Oi! Me conta o que você quer pedir hoje.", nome);

        act.Should().NotThrow();
        Avaliar("Oi! Me conta o que você quer pedir hoje.", nome).Should().BeNull();
    }
}
