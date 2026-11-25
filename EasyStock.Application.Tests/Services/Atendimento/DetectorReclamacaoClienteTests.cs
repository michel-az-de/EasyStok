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

    [Theory]
    [InlineData("manda o link pra eu pagar")]
    [InlineData("quero uma lasanha")]
    [InlineData("")]
    public void ConversaComumNaoEhReclamacao(string texto) =>
        DetectorReclamacaoCliente.EhReclamacao([texto]).Should().BeFalse();
}
