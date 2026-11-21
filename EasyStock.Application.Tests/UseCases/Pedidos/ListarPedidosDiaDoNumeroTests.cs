using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ListarPedidosCliente;

namespace EasyStock.Application.Tests.UseCases.Pedidos;

/// <summary>
/// #2478: a lista de Pedidos mostra "Nº 007 de 15/11". O número vale no dia de produção, que numa encomenda não é o
/// dia em que o pedido foi feito; por isso o DTO leva <c>DataNumero</c> junto de <c>NumeroDoDia</c>.
/// </summary>
public class ListarPedidosDiaDoNumeroTests
{
    [Fact]
    public async Task ListaLevaODiaDeProducaoDoNumero()
    {
        var empresaId = Guid.NewGuid();
        var encomenda = Pedido.Criar(empresaId);
        encomenda.DefinirNumeroDoDia(new DateOnly(2026, 11, 15), 7);
        var semNumero = Pedido.Criar(empresaId);
        var repo = Substitute.For<IPedidoRepository>();
        repo.GetByEmpresaAsync(empresaId, 1, 20, Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns((new[] { encomenda, semNumero }.AsEnumerable(), 2));

        var lista = await new ListarPedidosUseCase(repo).ExecuteAsync(new ListarPedidosQuery(empresaId));

        lista.Items[0].NumeroDoDia.Should().Be(7);
        lista.Items[0].DataNumero.Should().Be(new DateOnly(2026, 11, 15));
        lista.Items[1].DataNumero.Should().BeNull();
    }
}
