using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #2992: critério determinístico da desistência do cliente, por texto normalizado (sem acento, minúsculas,
/// pontuação virada espaço). Frase que pede o cancelamento ou diz que não quer mais o pedido conta; pergunta
/// sobre o pedido, negação e "não quero mais" seguido de outra coisa não contam.
/// </summary>
public class DetectorDesistenciaPedidoTests
{
    [Theory]
    [InlineData("Não quero mais o pedido")]
    [InlineData("não quero mais o pedido, obrigada")]
    [InlineData("Mas eu não quero mais")]
    [InlineData("mas eu nao quero mais.")]
    [InlineData("não quero mais isso")]
    [InlineData("Pode cancelar")]
    [InlineData("pode cancelar, por favor!")]
    [InlineData("cancela o pedido")]
    [InlineData("Cancele o pedido, mudei de ideia")]
    [InlineData("quero cancelar o pedido")]
    [InlineData("Desisti")]
    [InlineData("eu desisti do pedido")]
    public void FraseDeDesistenciaPausa(string texto)
    {
        DetectorDesistenciaPedido.EhDesistencia(texto).Should().BeTrue(texto);
    }

    [Theory]
    [InlineData("Quero um molho de tomate")]
    [InlineData("não quero mais molho")]
    [InlineData("não quero mais nada, só o troco")]
    [InlineData("Oi, tudo bem?")]
    [InlineData("Quanto custa o pedido?")]
    [InlineData("Meu pedido já saiu?")]
    [InlineData("Não desisti")]
    [InlineData("nao pode cancelar")]
    [InlineData("não cancela o pedido")]
    [InlineData("não quero cancelar o pedido")]
    [InlineData("como cancelo o pedido?")]
    [InlineData("")]
    [InlineData(null)]
    public void MensagemComumNaoPausa(string? texto)
    {
        DetectorDesistenciaPedido.EhDesistencia(texto).Should().BeFalse(texto ?? "(nulo)");
    }
}
