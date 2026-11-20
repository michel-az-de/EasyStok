using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>
/// #2992: a desistência do cliente fica marcada na própria conversa (no <c>ContextoJson</c>, sem coluna nova),
/// amarrada ao pedido. Só vale com a conversa assumida pela equipe e com o pedido em andamento da conversa.
/// </summary>
public class ConversaDesistenciaPedidoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private static Conversa AssumidaComPedido(Guid pedidoId)
    {
        var conversa = Conversa.Abrir(Empresa, "5511999990002", Agora, contatoNome: "Tatiana");
        conversa.RegistrarEntrada(Agora);
        conversa.Assumir(Agora.AddMinutes(1), Guid.NewGuid());
        conversa.DefinirPedidoEmAndamento(pedidoId);
        return conversa;
    }

    [Fact]
    public void DesistenciaMarcaOPedidoDaConversaAssumida()
    {
        var pedidoId = Guid.NewGuid();
        var conversa = AssumidaComPedido(pedidoId);

        conversa.MarcarClienteDesistiuDoPedido(pedidoId).Should().BeTrue("a primeira desistência do pedido é marcada");

        conversa.ClienteDesistiuDoPedido(pedidoId).Should().BeTrue();
        conversa.ContextoJson.Should().Contain("clienteDesistiuPedidoId");
    }

    [Fact]
    public void SegundaDesistenciaDoMesmoPedidoNaoMarcaDeNovo()
    {
        var pedidoId = Guid.NewGuid();
        var conversa = AssumidaComPedido(pedidoId);
        conversa.MarcarClienteDesistiuDoPedido(pedidoId);

        conversa.MarcarClienteDesistiuDoPedido(pedidoId).Should().BeFalse("a equipe já foi avisada uma vez");
    }

    [Fact]
    public void ConversaAutomaticaNaoMarcaDesistencia()
    {
        var pedidoId = Guid.NewGuid();
        var conversa = Conversa.Abrir(Empresa, "5511999990003", Agora, contatoNome: "Tatiana");
        conversa.RegistrarEntrada(Agora);
        conversa.DefinirPedidoEmAndamento(pedidoId);

        conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        conversa.MarcarClienteDesistiuDoPedido(pedidoId).Should().BeFalse("só a equipe assumida recebe o aviso da desistência");
        conversa.ClienteDesistiuDoPedido(pedidoId).Should().BeFalse();
    }

    [Fact]
    public void DesistenciaNaoValeParaOutroPedidoDaConversa()
    {
        var pedidoId = Guid.NewGuid();
        var conversa = AssumidaComPedido(pedidoId);
        conversa.MarcarClienteDesistiuDoPedido(pedidoId);

        conversa.ClienteDesistiuDoPedido(Guid.NewGuid()).Should().BeFalse("um pedido novo da mesma conversa segue normal");
    }

    [Fact]
    public void ContextoSemChaveNaoEhDesistencia()
    {
        var conversa = AssumidaComPedido(Guid.NewGuid());
        conversa.DefinirContexto("{\"outra\":true}");

        conversa.ClienteDesistiuDoPedido(Guid.NewGuid()).Should().BeFalse();
    }
}
