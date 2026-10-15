using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inventario.Desacertos;

namespace EasyStock.Application.Tests.UseCases.Inventario.Desacertos;

/// <summary>S22 (#1181): alerta de desacerto é projeção de <c>QuantidadeDescoberta</c>, texto cita os pedidos.</summary>
public class ListarDesacertosEstoqueUseCaseTests
{
    [Fact]
    public async Task TextoCitaPedidos()
    {
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var pedidoA = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000001");
        var pedidoB = Guid.Parse("d4e5f6a7-0000-0000-0000-000000000002");
        var em = new DateTime(2026, 9, 22, 15, 0, 0, DateTimeKind.Utc);
        var queries = Substitute.For<IDesacertosEstoqueQueries>();
        queries.ListarAsync(empresaId, null, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new DesacertoProdutoLinha(produtoId, "Lasanha", 2m, 0m, new[]
            {
                new SaidaDesacertoLinha(em, 1m, $"{pedidoA}:{Guid.NewGuid()}"),
                new SaidaDesacertoLinha(em.AddMinutes(30), 1m, $"{pedidoB}:{Guid.NewGuid()}"),
                new SaidaDesacertoLinha(em.AddMinutes(40), 1m, $"{pedidoB}:{Guid.NewGuid()}"),
            }),
        });

        var resultado = await new ListarDesacertosEstoqueUseCase(queries)
            .ExecuteAsync(new ListarDesacertosEstoqueInput(empresaId));

        var d = resultado.Should().ContainSingle().Subject;
        d.ProdutoId.Should().Be(produtoId);
        d.QuantidadeDescoberta.Should().Be(2m);
        d.Pedidos.Should().Equal("A1B2C3D4", "D4E5F6A7");
        d.Texto.Should().Be("Vendeu 2 de Lasanha sem produção lançada em 22/09 (pedidos #A1B2C3D4, #D4E5F6A7)");
        d.PrimeiroEm.Should().Be(em);
        d.UltimoEm.Should().Be(em.AddMinutes(40));
    }

    [Fact]
    public async Task SemPedidoIdentificadoTextoOmitePedidos()
    {
        var empresaId = Guid.NewGuid();
        var queries = Substitute.For<IDesacertosEstoqueQueries>();
        queries.ListarAsync(empresaId, null, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new DesacertoProdutoLinha(Guid.NewGuid(), "Bolo", 1.5m, 0m, Array.Empty<SaidaDesacertoLinha>()),
        });

        var resultado = await new ListarDesacertosEstoqueUseCase(queries)
            .ExecuteAsync(new ListarDesacertosEstoqueInput(empresaId));

        resultado.Single().Texto.Should().Be("Vendeu 1,5 de Bolo sem produção lançada");
        resultado.Single().Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task ExigeEmpresa()
    {
        var uc = new ListarDesacertosEstoqueUseCase(Substitute.For<IDesacertosEstoqueQueries>());
        Func<Task> act = () => uc.ExecuteAsync(new ListarDesacertosEstoqueInput(Guid.Empty));
        await act.Should().ThrowAsync<Exception>();
    }
}
