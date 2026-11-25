using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #3356 (R11): <see cref="PortoesDoAssistente.EtapasAsync"/> é o único lugar com a ordem dos cinco grupos (conversa, loja,
/// pessoa, limite, cota). Quem consome o fluxo inteiro (a tela) lê tudo; quem para na primeira etapa que barra (a retomada)
/// não lê o grupo seguinte. A preguiça é entre grupos: o grupo da loja é lido inteiro, como no turno.
/// </summary>
public class PortoesDoAssistenteEtapasTests
{
    private const string WaId = "5511999998888";
    private static readonly DateTime Agora = new(2026, 9, 26, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly ITurnoAgenteRepository _turnos = Substitute.For<ITurnoAgenteRepository>();
    private readonly IContatoDaEquipeAvisada _equipe = Substitute.For<IContatoDaEquipeAvisada>();
    private readonly ConfiguracaoAtendimento _configuracao;
    private readonly Conversa _conversa;

    public PortoesDoAssistenteEtapasTests()
    {
        _configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        _configuracao.Ativo = true;
        _llm.DisponivelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _conversa = Conversa.Abrir(_empresaId, WaId, Agora.AddMinutes(-1), "Maria");
        _conversa.RegistrarEntrada(Agora.AddMinutes(-1));
    }

    private PortoesDoAssistente Portoes() => new(
        _llm, _conversas, _turnos, Options.Create(new LimiteRespostasAgenteOptions { MaximoPorHora = 10 }), _equipe);

    private IAsyncEnumerable<EtapaAssistente> Etapas(Cliente? cliente = null, CancellationToken ct = default) =>
        Portoes().EtapasAsync(_empresaId, _conversa, _configuracao, cliente, Agora, ct);

    private async Task<List<EtapaAssistente>> Todas(Cliente? cliente = null)
    {
        var etapas = new List<EtapaAssistente>();
        await foreach (var etapa in Etapas(cliente)) etapas.Add(etapa);
        return etapas;
    }

    private async Task<EtapaAssistente?> AtePrimeiraQueBarra(Cliente? cliente = null)
    {
        await foreach (var etapa in Etapas(cliente))
            if (etapa.Barra) return etapa;
        return null;
    }

    private async Task<List<EtapaAssistente>> Primeiras(int quantas)
    {
        var etapas = new List<EtapaAssistente>();
        await foreach (var etapa in Etapas())
        {
            etapas.Add(etapa);
            if (etapas.Count == quantas) break;
        }
        return etapas;
    }

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

    private static IEnumerable<string> CodigosDoCatalogo => PortoesDoAssistente.Catalogo.Select(p => p.Codigo);

    private async Task NaoLeuALoja()
    {
        await _llm.DidNotReceiveWithAnyArgs().DisponivelAsync(default, default);
        await _equipe.DidNotReceiveWithAnyArgs().EhInscritoAsync(default, default, default);
    }

    private async Task NaoLeuOLimiteNemACota()
    {
        await _conversas.DidNotReceiveWithAnyArgs().ContarRespostasDoAgenteDesdeAsync(default, default, default, default);
        await _turnos.DidNotReceiveWithAnyArgs().SomarAsync(default, default, default, default);
    }

    [Fact]
    public async Task TudoPassando_DevolveAsOnzeEtapasNaOrdemDoCatalogo()
    {
        CotaEUso(cota: 1_000, usados: 10);

        var etapas = await Todas(Liberado());

        etapas.Select(e => e.Codigo).Should().Equal(CodigosDoCatalogo);
        etapas.Should().NotContain(e => e.Barra);
    }

    [Fact]
    public async Task SemClienteESemCota_AsOnzeEtapasContinuamNaOrdemDoCatalogo()
    {
        var etapas = await Todas();

        etapas.Select(e => e.Codigo).Should().Equal(CodigosDoCatalogo);
        etapas.Single(e => e.Codigo == PortoesDoAssistente.Cota).Estado.Should().Be(EstadoEtapaAssistente.NaoSeAplica);
    }

    [Fact]
    public async Task ConsumidaInteira_ConversaQueBarraNaoEsconde_AsEtapasSeguintes()
    {
        // A tela mostra tudo: quem consome até o fim lê todos os grupos, mesmo com o primeiro barrando.
        _conversa.Assumir(Agora.AddMinutes(-1), Guid.NewGuid());
        CotaEUso(cota: 1_000, usados: 10);

        var etapas = await Todas(Liberado());

        etapas.Select(e => e.Codigo).Should().Equal(CodigosDoCatalogo);
        etapas[0].Codigo.Should().Be(PortoesDoAssistente.Situacao);
        etapas[0].Barra.Should().BeTrue();
        await _llm.Received(1).DisponivelAsync(_empresaId, Arg.Any<CancellationToken>());
        await _conversas.Received(1).ContarRespostasDoAgenteDesdeAsync(
            _empresaId, _conversa.Id, Agora.AddHours(-1), Arg.Any<CancellationToken>());
        await _turnos.Received(1).SomarAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConversaQueBarra_QuemParaNaPrimeira_NaoLeALojaNemOResto()
    {
        _conversa.Assumir(Agora.AddMinutes(-1), Guid.NewGuid());

        var barrou = await AtePrimeiraQueBarra(Liberado());

        barrou!.Codigo.Should().Be(PortoesDoAssistente.Situacao);
        await NaoLeuALoja();
        await NaoLeuOLimiteNemACota();
    }

    [Fact]
    public async Task QuemParaDepoisDaConversa_NaoLeALoja()
    {
        var tres = await Primeiras(3);

        tres.Select(e => e.Codigo).Should().Equal(
            PortoesDoAssistente.Situacao, PortoesDoAssistente.Vinculo, PortoesDoAssistente.Janela);
        await NaoLeuALoja();
        await NaoLeuOLimiteNemACota();
    }

    [Fact]
    public async Task ChaveDesligada_QuemParaNaPrimeira_LeAlojaInteiraMasNaoOCliente()
    {
        _configuracao.Ativo = false;
        CotaEUso(cota: 1_000, usados: 10);

        var barrou = await AtePrimeiraQueBarra(Bloqueado());

        barrou!.Codigo.Should().Be(PortoesDoAssistente.Chave);
        // O grupo da loja sai pronto (modelo, canal e contato da equipe), como no turno: a preguiça é entre grupos.
        await _llm.Received(1).DisponivelAsync(_empresaId, Arg.Any<CancellationToken>());
        await _equipe.Received(1).EhInscritoAsync(_empresaId, WaId, Arg.Any<CancellationToken>());
        await NaoLeuOLimiteNemACota();
    }

    [Fact]
    public async Task ClienteBloqueado_QuemParaNaPrimeira_NaoLeOLimiteNemACota()
    {
        CotaEUso(cota: 1_000, usados: 1_000);

        var barrou = await AtePrimeiraQueBarra(Bloqueado());

        barrou!.Codigo.Should().Be(PortoesDoAssistente.Bloqueado);
        await NaoLeuOLimiteNemACota();
    }

    [Fact]
    public async Task LimiteEstourado_QuemParaNaPrimeira_NaoSomaOMes()
    {
        _conversas.ContarRespostasDoAgenteDesdeAsync(_empresaId, _conversa.Id, Agora.AddHours(-1), Arg.Any<CancellationToken>())
            .Returns(10);
        CotaEUso(cota: 1_000, usados: 1_000);

        var barrou = await AtePrimeiraQueBarra();

        barrou!.Codigo.Should().Be(PortoesDoAssistente.Limite);
        await _turnos.DidNotReceiveWithAnyArgs().SomarAsync(default, default, default, default);
    }

    [Fact]
    public async Task SoACotaEsgotada_ForaDeTodoOResto_BarraNaUltima()
    {
        CotaEUso(cota: 1_000, usados: 1_000);

        var barrou = await AtePrimeiraQueBarra(Liberado());

        barrou!.Codigo.Should().Be(PortoesDoAssistente.Cota);
        barrou.MotivoEscalada.Should().Be(PortoesDoAssistente.MotivoCotaEsgotada);
    }

    [Fact]
    public async Task OTokenDeQuemConsome_ChegaAoModeloEAoBanco()
    {
        CotaEUso(cota: 1_000, usados: 10);
        using var origem = new CancellationTokenSource();

        await foreach (var _ in Etapas(Liberado(), origem.Token)) { }

        await _llm.Received(1).DisponivelAsync(_empresaId, origem.Token);
        await _equipe.Received(1).EhInscritoAsync(_empresaId, WaId, origem.Token);
        await _conversas.Received(1).ContarRespostasDoAgenteDesdeAsync(_empresaId, _conversa.Id, Agora.AddHours(-1), origem.Token);
        await _turnos.Received(1).SomarAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), origem.Token);
    }
}
