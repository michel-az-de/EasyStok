using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ListarPedidosCliente;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.Pedidos;

/// <summary>
/// #3294: a linha da lista de Pedidos leva os 3 primeiros itens na ordem em que entraram, a ordem da compra do
/// cliente e desde quando está na situação. Os sinais vêm numa leitura só para a página inteira.
/// </summary>
public class ListarPedidosSinaisDaLinhaTests
{
    private static readonly DateTime Agora = new(2026, 11, 24, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LinhaLevaItensNaOrdemCompraDoClienteESituacao()
    {
        var empresaId = Guid.NewGuid();
        var inicio = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var comItens = Pedido.Criar(empresaId);
        foreach (var (nome, minuto) in new[] { ("Torta", 3), ("Bolo", 1), ("Quiche", 2), ("Pão de mel", 4) })
            comItens.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = comItens.Id, Nome = nome, Quantidade = 2m, Unidade = "un", CriadoEm = inicio.AddMinutes(minuto) });
        // O frete entra primeiro e não é item pedido (#2018): fica fora do resumo e da conta.
        comItens.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = comItens.Id, Nome = PedidoItem.PrefixoDoFrete + "Centro", Quantidade = 1m, CriadoEm = inicio });
        comItens.AgendadoParaEm = Agora.AddMinutes(-40);
        var semSinal = Pedido.Criar(empresaId);
        semSinal.AgendadoParaEm = Agora.AddMinutes(40);
        var repo = Substitute.For<IPedidoRepository>();
        repo.GetByEmpresaAsync(empresaId, 1, 20, Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns((new[] { comItens, semSinal }.AsEnumerable(), 2));
        var resumo = Substitute.For<IPedidosResumoQuery>();
        var naSituacao = inicio.AddMinutes(30);
        resumo.SinaisAsync(empresaId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, SinaisDoPedido> { [comItens.Id] = new(5, naSituacao) });

        var lista = await new ListarPedidosUseCase(repo, resumo, new FakeTimeProvider(new DateTimeOffset(Agora))).ExecuteAsync(new ListarPedidosQuery(empresaId, ComSinais: true));

        lista.Items[0].ItensResumo!.Select(i => i.Nome).Should().Equal("Bolo", "Quiche", "Torta");
        lista.Items[0].ItensSemFrete.Should().Be(4, "a tela mostra +1 pelos itens sem o frete");
        lista.Items[0].ItensCount.Should().Be(5, "o contador de sempre não muda");
        lista.Items[0].CompraDoCliente.Should().Be(5);
        lista.Items[0].SituacaoDesde.Should().Be(naSituacao);
        lista.Items[0].Atrasado.Should().BeTrue("a hora marcada passou há 40 minutos");
        lista.Items[1].Atrasado.Should().BeFalse();
        lista.Items[1].CompraDoCliente.Should().BeNull();
        lista.Items[1].SituacaoDesde.Should().Be(semSinal.CriadoEm, "sem evento vale a criação");
        await resumo.Received(1).SinaisAsync(empresaId,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { comItens.Id, semSinal.Id })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PaginaVaziaNaoPedeSinais()
    {
        var empresaId = Guid.NewGuid();
        var repo = Substitute.For<IPedidoRepository>();
        repo.GetByEmpresaAsync(empresaId, 1, 20, Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns((Enumerable.Empty<Pedido>(), 0));
        var resumo = Substitute.For<IPedidosResumoQuery>();

        (await new ListarPedidosUseCase(repo, resumo, new FakeTimeProvider(new DateTimeOffset(Agora))).ExecuteAsync(new ListarPedidosQuery(empresaId, ComSinais: true))).Items.Should().BeEmpty();
        await resumo.DidNotReceiveWithAnyArgs().SinaisAsync(default, default!, default);
    }

    [Fact]
    public async Task SemPedirSinaisALinhaFicaComoAntes()
    {
        var empresaId = Guid.NewGuid();
        var pedido = Pedido.Criar(empresaId);
        pedido.AgendadoParaEm = Agora.AddHours(-1);
        var repo = Substitute.For<IPedidoRepository>();
        repo.GetByEmpresaAsync(empresaId, 1, 20, Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<DateTime?>(), Arg.Any<DateTime?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns((new[] { pedido }.AsEnumerable(), 1));
        var resumo = Substitute.For<IPedidosResumoQuery>();

        var linha = (await new ListarPedidosUseCase(repo, resumo, new FakeTimeProvider(new DateTimeOffset(Agora))).ExecuteAsync(new ListarPedidosQuery(empresaId))).Items.Single();

        linha.ItensResumo.Should().BeNull("Web e Cozinha não pagam a leitura dos sinais");
        linha.Atrasado.Should().BeNull();
        linha.SituacaoDesde.Should().BeNull();
        await resumo.DidNotReceiveWithAnyArgs().SinaisAsync(default, default!, default);
    }
}
