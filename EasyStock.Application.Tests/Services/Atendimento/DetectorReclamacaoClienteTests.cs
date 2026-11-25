using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

public class DetectorReclamacaoClienteTests
{
    // #3340 (R2I-05): cobrança de dinheiro ou ameaça de Procon vai à dona sem o modelo.
    [Theory]
    [InlineData("quero o estorno do pedido")]
    [InlineData("O pedido veio errado, quero meu dinheiro de volta")]
    [InlineData("vou no Procon")]
    [InlineData("quero reembolso agora")]
    public void CobrancaDeDinheiroOuProconEhReclamacao(string texto) =>
        DetectorReclamacaoCliente.EhReclamacao([texto]).Should().BeTrue();

    // Revisão da PR: quem só pergunta do estorno recebe a resposta do histórico (o assistente lê o ledger), não vai à dona.
    [Theory]
    [InlineData("Meu estorno foi confirmado?")]
    [InlineData("quando cai o reembolso?")]
    [InlineData("o estorno já caiu na conta?")]
    public void PerguntaSobreOEstornoNaoEhReclamacao(string texto) =>
        DetectorReclamacaoCliente.EhReclamacao([texto]).Should().BeFalse();

    [Theory]
    [InlineData("estorne meu pedido agora")]
    [InlineData("me reembolsem")]
    [InlineData("preciso do estorno hoje")]
    public void PedidoDeEstornoEmImperativoEhReclamacao(string texto) =>
        DetectorReclamacaoCliente.EhReclamacao([texto]).Should().BeTrue();

    [Theory]
    [InlineData("manda o link pra eu pagar")]
    [InlineData("quero uma lasanha")]
    [InlineData("")]
    public void ConversaComumNaoEhReclamacao(string texto) =>
        DetectorReclamacaoCliente.EhReclamacao([texto]).Should().BeFalse();
}
