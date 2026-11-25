using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #3298 (R11): pessoa, limite de respostas e cota do mês num método só, na ordem do turno e parando na primeira etapa
/// que barra. O turno, a retomada e a resposta sem modelo usam o mesmo, então a ordem e a parada valem para os três.
/// </summary>
public class PortoesDoAssistentePrimeiraQueBarraTests
{
    private static readonly DateTime Agora = new(2026, 9, 26, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _conversaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ITurnoAgenteRepository _turnos = Substitute.For<ITurnoAgenteRepository>();
    private readonly ConfiguracaoAtendimento _configuracao;

    public PortoesDoAssistentePrimeiraQueBarraTests()
    {
        _configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        _configuracao.Ativo = true;
    }

    private PortoesDoAssistente Portoes(int limitePorHora = 10) => new(
        Substitute.For<IAgenteLlmClient>(), _conversas, _turnos,
        Options.Create(new LimiteRespostasAgenteOptions { MaximoPorHora = limitePorHora }));

    private Task<EtapaAssistente?> Primeira(Cliente? cliente) =>
        Portoes().PrimeiraQueBarraDoClienteEDoCustoAsync(_empresaId, _conversaId, _configuracao, cliente, Agora, CancellationToken.None);

    private void RespostasNaUltimaHora(int total) =>
        _conversas.ContarRespostasDoAgenteDesdeAsync(_empresaId, _conversaId, Agora.AddHours(-1), Arg.Any<CancellationToken>())
            .Returns(total);

    private void CotaEUso(long cota, long usados)
    {
        _configuracao.DefinirAssistente(null, null, cota, null, null);
        _turnos.SomarAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new SomaTurnosAgente(3, usados, 0, 0, 0m, 0));
    }

    private static Cliente Liberado() => Cliente.Criar(Guid.NewGuid(), "Maria");

    private static Cliente Bloqueado()
    {
        var cliente = Liberado();
        cliente.Bloquear("golpe", Agora.AddHours(-1));
        return cliente;
    }

    private static Cliente Encaminhado()
    {
        var cliente = Liberado();
        cliente.PausarAtendimentoAutomatico("orçamento", Agora.AddDays(-1));
        return cliente;
    }

    [Fact]
    public async Task NadaBarra_DevolveNulo()
    {
        RespostasNaUltimaHora(0);
        CotaEUso(cota: 1_000, usados: 10);

        (await Primeira(Liberado())).Should().BeNull();
    }

    [Fact]
    public async Task ClienteBloqueado_BarraAntesDoLimiteEDaCota_SemConsultarOBanco()
    {
        RespostasNaUltimaHora(99);
        CotaEUso(cota: 1_000, usados: 1_000);

        var barrou = await Primeira(Bloqueado());

        barrou.Should().NotBeNull();
        barrou!.Codigo.Should().Be(PortoesDoAssistente.Bloqueado);
        barrou.MotivoEscalada.Should().NotBeNullOrWhiteSpace();
        await _conversas.DidNotReceiveWithAnyArgs().ContarRespostasDoAgenteDesdeAsync(default, default, default, default);
        await _turnos.DidNotReceiveWithAnyArgs().SomarAsync(default, default, default, default);
    }

    [Fact]
    public async Task ClienteEncaminhado_Barra()
    {
        RespostasNaUltimaHora(0);
        CotaEUso(cota: 1_000, usados: 10);

        var barrou = await Primeira(Encaminhado());

        barrou!.Codigo.Should().Be(PortoesDoAssistente.Pausado);
        barrou.MotivoEscalada.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LimiteEstourado_BarraAntesDaCota_SemSomarOMes()
    {
        RespostasNaUltimaHora(10);
        CotaEUso(cota: 1_000, usados: 1_000);

        var barrou = await Primeira(cliente: null);

        barrou!.Codigo.Should().Be(PortoesDoAssistente.Limite);
        barrou.MotivoEscalada.Should().Be(AgenteAtendimentoService.MotivoLimiteRespostas(10));
        await _turnos.DidNotReceiveWithAnyArgs().SomarAsync(default, default, default, default);
    }

    [Fact]
    public async Task SoACotaEsgotada_Barra()
    {
        RespostasNaUltimaHora(0);
        CotaEUso(cota: 1_000, usados: 1_000);

        var barrou = await Primeira(Liberado());

        barrou!.Codigo.Should().Be(PortoesDoAssistente.Cota);
        barrou.MotivoEscalada.Should().Be(PortoesDoAssistente.MotivoCotaEsgotada);
    }
}
