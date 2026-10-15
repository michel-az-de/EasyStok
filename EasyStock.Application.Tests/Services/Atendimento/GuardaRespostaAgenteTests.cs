using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

public class GuardaRespostaAgenteTests
{
    private static readonly string[] Ferramentas = ["consultar_cardapio", "criar_pedido", "escalar_para_dona"];

    private static string? Avaliar(string texto, params string[] fontes) =>
        GuardaRespostaAgente.Avaliar(texto, fontes, Ferramentas);

    [Theory]
    [InlineData("Oi! Me conta o que você quer pedir hoje.")]
    [InlineData("Para a entrega, me passa o endereço com CEP e número?")]
    [InlineData("Quando o pedido for confirmado, você recebe o link de pagamento por aqui.")]
    [InlineData("Assim que o pagamento for aprovado a cozinha começa a preparar.")]
    [InlineData("Não temos frete grátis, a taxa depende do CEP.")]
    [InlineData("Qual o tamanho da lasanha: 600 g, 800 g ou 1 kg?")]
    public void ConversaSemAfirmacaoPassa(string texto) => Avaliar(texto).Should().BeNull();

    [Theory]
    [InlineData("Sua encomenda está confirmada para sábado.")]
    [InlineData("Vou pedir para a dona verificar isso com você.")]
    [InlineData("Vou verificar a lasanha com a dona e já te passo o valor correto.")]
    [InlineData("Não se preocupe, seu pedido já está confirmado.")]
    [InlineData("Sem problema, o frete é grátis para você.")]
    [InlineData("A lasanha custa 59 reais.")]
    [InlineData("Vou te dar 10 reais de desconto.")]
    [InlineData("Quando o pedido for confirmado, seu pagamento está aprovado.")]
    [InlineData("Seu pedido já está registrado.")]
    [InlineData("Anotei seu pedido aqui.")]
    [InlineData("Pedido concluído e conversa encerrada.")]
    [InlineData("Seu pedido já saiu para entrega.")]
    [InlineData("O PIX foi identificado.")]
    [InlineData("Seu estorno já caiu na conta.")]
    [InlineData("A entrega sai gratuita hoje.")]
    [InlineData("Consigo um desconto de 10 reais.")]
    [InlineData("Vou chamar criar_pedido agora.")]
    [InlineData("```json\n{}\n```")]
    [InlineData("   ")]
    public void AfirmacaoSemFonteEBarrada(string texto) => Avaliar(texto).Should().NotBeNull();

    [Theory]
    [InlineData("A lasanha sai por R$ 38,00.", """{"preco":"R$ 38,00"}""", true)]
    [InlineData("Três lasanhas ficam R$ 114,00.", """{"preco":"R$ 38,00"}""", false)]
    [InlineData("O total é R$ 1.234,56.", "Total: R$ 1.234,56", true)]
    [InlineData("Fica R$ 38.", "preco R$ 38,00", true)]
    [InlineData("A lasanha sai por R$ 39,00.", """{"preco":"R$ 38,00"}""", false)]
    [InlineData("Fica R$ 57,00.", """{"preco":"R$ 38,00"}""", false)]
    public void ValorSoPassaQuandoVemExatamenteDaFonte(string texto, string fonte, bool passa) =>
        (Avaliar(texto, fonte) is null).Should().Be(passa);

    [Fact]
    public void LinkSoPassaQuandoEstaNumaFonte()
    {
        Avaliar("Veja o cardápio: https://casadababa.com/cardapio.", "Cardápio: https://casadababa.com/cardapio").Should().BeNull();
        Avaliar("Veja: https://outro.exemplo/x", "Cardápio: https://casadababa.com/cardapio").Should().Contain("link");
    }
}
