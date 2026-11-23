using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Cliente;
using EasyStock.Application.UseCases.ListarClientes;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>#3244 (S1): a lista devolve a relação do cliente calculada no servidor, num record próprio.</summary>
public class ListarClientesRelacaoTests
{
    [Fact]
    public void Record_da_lista_traz_todos_os_campos_do_ClienteResult()
    {
        var esperados = typeof(ClienteResult).GetProperties().Where(p => p.Name != "EqualityContract")
            .Select(p => (p.Name, p.PropertyType));
        var obtidos = typeof(ClienteListaResult).GetProperties().Select(p => (p.Name, p.PropertyType)).ToList();

        obtidos.Should().Contain(esperados);
    }

    [Fact]
    public async Task Monta_ticket_medio_estagio_e_carteira_a_partir_da_relacao()
    {
        var empresaId = Guid.NewGuid();
        var maria = Cliente.Criar(empresaId, "Maria Ficticia");
        var sem = Cliente.Criar(empresaId, "Joana Ficticia");
        var primeira = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var ultima = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var repo = Substitute.For<IClienteRepository>();
        repo.ListarComRelacaoAsync(empresaId, 1, 20, null, null, "gasto", "desc", null, Arg.Any<CancellationToken>())
            .Returns(new ListaDeClientes(
                [
                    new ClienteRelacao(maria, 3, 100m, primeira, ultima, 1, 2, 4, "Gosta de lasanha", ["vip", "vegano"], true),
                    new ClienteRelacao(sem, 0, 0m, null, null, 0, 0, 0, null, [], false),
                ], 2, 250.50m));

        var r = await new ListarClientesUseCase(repo).ExecuteAsync(
            new ListarClientesQuery(empresaId, Sort: "gasto", Order: "desc"));

        r.GastoTotalDaCarteira.Should().Be(250.50m);
        r.Total.Should().Be(2);
        var m = r.Items[0];
        (m.Id, m.GastoTotal, m.Compras, m.TicketMedio, m.PrimeiraCompraEm, m.UltimaCompraEm).Should()
            .Be((maria.Id, 100m, 3, 33.33m, primeira, ultima));
        (m.OcorrenciasAbertas, m.OcorrenciasTotal, m.NotasTotal, m.UltimaNota, m.Estagio, m.EmRisco).Should()
            .Be((1, 2, 4, "Gosta de lasanha", "recorrente", true));
        m.Tags.Should().Equal("vip", "vegano");
        var s = r.Items[1];
        (s.TicketMedio, s.Estagio, s.EmRisco).Should().Be((0m, "sem_pedido", false));
    }

    [Fact]
    public async Task Ticket_medio_arredonda_meio_para_longe_do_zero_pela_regra_do_dinheiro()
    {
        var empresaId = Guid.NewGuid();
        var repo = Substitute.For<IClienteRepository>();
        repo.ListarComRelacaoAsync(empresaId, 1, 20, null, null, "nome", "asc", null, Arg.Any<CancellationToken>())
            .Returns(new ListaDeClientes(
                [new ClienteRelacao(Cliente.Criar(empresaId, "Ana Ficticia"), 4, 20.50m, null, null, 0, 0, 0, null, [], false)],
                1, 20.50m));

        var r = await new ListarClientesUseCase(repo).ExecuteAsync(new ListarClientesQuery(empresaId));

        r.Items.Single().TicketMedio.Should().Be(5.13m, "20,50 / 4 = 5,125 e a regra #2373 arredonda o meio para longe do zero");
    }
}
