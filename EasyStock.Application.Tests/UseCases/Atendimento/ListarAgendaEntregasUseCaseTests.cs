using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

public sealed class ListarAgendaEntregasUseCaseTests
{
    [Fact]
    public async Task ContaReservasAtivasEExibeBloqueioDoDia()
    {
        var empresaId = Guid.NewGuid();
        var data = new DateOnly(2026, 10, 20);
        var loja = StorefrontEntity.Criar(empresaId, "casa-da-baba", "Casa da Babá", 0);
        loja.Ativar();
        var janela = JanelaEntrega.Criar(loja.Id, (int)data.DayOfWeek, new(9, 0), new(12, 0), 3, "Manhã");
        var bloqueio = BloqueioEntrega.Criar(loja.Id, data, "Feriado");
        var pedidoId = Guid.NewGuid();
        var lojas = Substitute.For<IStorefrontRepository>();
        var janelas = Substitute.For<IJanelaEntregaRepository>();
        var bloqueios = Substitute.For<IBloqueioEntregaRepository>();
        var vagas = Substitute.For<IVagaOcupadaRepository>();
        var pedidos = Substitute.For<IAgendaPedidosQuery>();
        lojas.GetByEmpresaAsync(empresaId, Arg.Any<CancellationToken>()).Returns(loja);
        janelas.GetTodasDoStorefrontAsync(loja.Id, Arg.Any<CancellationToken>())
            .Returns(new List<JanelaEntrega> { janela });
        bloqueios.GetByStorefrontPeriodoAsync(loja.Id, data, data, Arg.Any<CancellationToken>())
            .Returns(new List<BloqueioEntrega> { bloqueio });
        vagas.ContarPorJanelaPeriodoAsync(Arg.Any<IReadOnlyList<Guid>>(), data, data, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), int> { [(janela.Id, data)] = 1 });
        pedidos.ListarAsync(empresaId, loja.Id, data, data, Arg.Any<CancellationToken>())
            .Returns(new List<AgendaPedidoLeitura> { new(janela.Id, data, pedidoId, "Cliente Teste", "aguardando") });

        var resultado = await new ListarAgendaEntregasUseCase(lojas, janelas, bloqueios, vagas, pedidos)
            .ExecuteAsync(empresaId, data, data);

        resultado.LojaDisponivel.Should().BeTrue();
        var dia = resultado.Dias.Should().ContainSingle().Which;
        dia.Bloqueio.Should().Be("Feriado");
        var faixa = dia.Janelas.Should().ContainSingle().Which;
        faixa.Ocupadas.Should().Be(1);
        faixa.Capacidade.Should().Be(3);
        faixa.Pedidos.Should().ContainSingle(p => p.PedidoId == pedidoId && p.ClienteNome == "Cliente Teste");
    }

    [Fact]
    public async Task RecusaPeriodoAcimaDe60DiasSemConsultarBanco()
    {
        var lojas = Substitute.For<IStorefrontRepository>();
        var useCase = new ListarAgendaEntregasUseCase(lojas, Substitute.For<IJanelaEntregaRepository>(),
            Substitute.For<IBloqueioEntregaRepository>(), Substitute.For<IVagaOcupadaRepository>(),
            Substitute.For<IAgendaPedidosQuery>());
        var inicio = new DateOnly(2026, 10, 10);

        var acao = () => useCase.ExecuteAsync(Guid.NewGuid(), inicio, inicio.AddDays(61));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        await lojas.DidNotReceiveWithAnyArgs().GetByEmpresaAsync(default, default);
    }
}
