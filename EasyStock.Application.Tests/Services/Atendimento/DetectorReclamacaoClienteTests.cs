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

    // Revisão dos deltas: outras formas da mesma cobrança (infinitivo, particípio, ameaça de pedir no cartão, demora do
    // estorno) também vão à dona.
    [Theory]
    [InlineData("quero estornar")]
    [InlineData("quero ser reembolsada")]
    [InlineData("vou pedir o estorno no cartão")]
    [InlineData("ainda não recebi o reembolso, já faz 10 dias")]
    [InlineData("o dinheiro não voltou")]
    public void OutrasFormasDaCobrancaTambemSaoReclamacao(string texto) =>
        DetectorReclamacaoCliente.EhReclamacao([texto]).Should().BeTrue();

    // Revisão dos deltas: "quero/preciso" seguido de "saber/ver/conferir" é consulta, não cobrança.
    [Theory]
    [InlineData("Quero saber do meu estorno")]
    [InlineData("Preciso saber quando cai o estorno")]
    [InlineData("quero conferir o reembolso")]
    public void QueroSaberDoEstornoEhConsultaNaoCobranca(string texto) =>
        DetectorReclamacaoCliente.EhReclamacao([texto]).Should().BeFalse();

    // Revisão dos deltas (I1): outras formas da consulta ("status", "me avisem", "confirme") também não são cobrança.
    [Theory]
    [InlineData("Quero o status do meu estorno")]
    [InlineData("Quero que me avisem quando cai o estorno")]
    [InlineData("Preciso que você confirme o estorno")]
    public void OutrasFormasDeConsultaDoEstornoNaoSaoCobranca(string texto) =>
        DetectorReclamacaoCliente.EhReclamacao([texto]).Should().BeFalse();

    // Revisão dos deltas (I2): o "pode confirmar?" de outra oração (depois da vírgula) não anula a cobrança.
    [Fact]
    public void CobrancaComPedidoDeConfirmacaoNaOutraOracaoContinuaReclamacao() =>
        DetectorReclamacaoCliente.EhReclamacao(["Quero meu estorno, pode confirmar?"]).Should().BeTrue();

    // Revisão dos deltas (I3): "dinheiro" como forma de pagamento, perto de "não recebi/chegou" sobre o pedido, não é cobrança.
    [Theory]
    [InlineData("Não recebi o pedido, é em dinheiro?")]
    [InlineData("Pago em dinheiro, o motoboy não chegou")]
    [InlineData("Não recebi o pedido em dinheiro")]
    public void DinheiroComoFormaDePagamentoNaoEhCobranca(string texto) =>
        DetectorReclamacaoCliente.EhReclamacao([texto]).Should().BeFalse();

    [Theory]
    [InlineData("meu dinheiro não voltou")]
    [InlineData("o estorno não caiu")]
    [InlineData("não caiu o dinheiro na conta")]
    public void DemoraDoDinheiroDoClienteContinuaReclamacao(string texto) =>
        DetectorReclamacaoCliente.EhReclamacao([texto]).Should().BeTrue();

    [Theory]
    [InlineData("manda o link pra eu pagar")]
    [InlineData("quero uma lasanha")]
    [InlineData("")]
    public void ConversaComumNaoEhReclamacao(string texto) =>
        DetectorReclamacaoCliente.EhReclamacao([texto]).Should().BeFalse();
}
