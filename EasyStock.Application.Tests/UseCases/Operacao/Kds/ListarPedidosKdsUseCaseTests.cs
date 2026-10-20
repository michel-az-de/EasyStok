using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Operacao.Kds;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Operacao.Kds;

public sealed class ListarPedidosKdsUseCaseTests
{
    [Fact]
    public async Task CardTrazNumeroComandaDaLeituraENuloSemConversa()
    {
        // #1883 (#1902): "Comanda N" é o número da conversa dona do pedido; pedido sem conversa não
        // inventa número. A referência curta (8 letras do id) continua no contrato.
        var empresaId = Guid.NewGuid();
        var hoje = HorarioBrasil.DataOperacional(DateTime.UtcNow);
        var comComanda = Leitura(hoje, numeroComanda: 12);
        var semComanda = Leitura(hoje, numeroComanda: null);
        var queries = Substitute.For<IKdsPedidoQueries>();
        queries.ListarAsync(empresaId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<KdsPedidoLeitura> { comComanda, semComanda });

        var cards = await new ListarPedidosKdsUseCase(queries, TimeProvider.System)
            .ExecuteAsync(new ListarPedidosKdsQuery(empresaId));

        cards.Should().HaveCount(2);
        cards.Single(c => c.Id == comComanda.Id).NumeroComanda.Should().Be(12);
        cards.Single(c => c.Id == semComanda.Id).NumeroComanda.Should().BeNull();
        cards.Should().OnlyContain(c => c.NumeroCurto == c.Id.ToString("N").Substring(0, 8).ToUpperInvariant());
    }

    private static KdsPedidoLeitura Leitura(DateOnly dataProducao, long? numeroComanda) => new(
        Id: Guid.NewGuid(),
        Status: StatusPedidoMapper.Aguardando,
        ClienteNome: "Cliente KDS",
        ClienteApt: null,
        Observacoes: null,
        AgendadoParaEm: null,
        CriadoEm: DateTime.UtcNow,
        PagoEm: null,
        DataProducao: dataProducao,
        InicioPrevistoEm: null,
        Janela: null,
        Itens: [new KdsItemLeitura("Brigadeiro", null, 1m, null, "paraServir", null)],
        NumeroComanda: numeroComanda);
}
