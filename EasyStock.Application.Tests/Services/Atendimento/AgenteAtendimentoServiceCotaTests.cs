using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// S69 (#2853): a cota mensal de tokens da loja vira etapa do Assistente. Em 100% o turno não chama o modelo, a
/// conversa passa para a equipe e o registro sai com desfecho cota. Sem cota, nada muda.
/// </summary>
public partial class AgenteAtendimentoServiceTests
{
    private ConfiguracaoAtendimento LojaComCota(long? cota, params string[] desligadas)
    {
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        configuracao.Ativo = true;
        configuracao.DefinirAssistente(null, null, cota, null, null);
        if (desligadas.Length > 0) configuracao.DefinirFerramentasDesligadas(FerramentasDaLoja.Serializar(desligadas));
        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns(configuracao);
        return configuracao;
    }

    private void UsoDoMes(long tokens) =>
        _turnos.SomarAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new SomaTurnosAgente(3, tokens, 0, 0, 0m, 0));

    [Fact]
    public async Task CotaEsgotada_NaoChamaOModelo_PassaParaAEquipeEGravaDesfechoCota()
    {
        LojaComCota(1_000);
        UsoDoMes(1_000);

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Should().Be(new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true));
        _requisicoes.Should().BeEmpty();
        await _escalador.Received(1).EscalarAsync(_empresaId, _conversa, PortoesDoAssistente.MotivoCotaEsgotada, Agora,
            Arg.Any<CancellationToken>());
        var turno = TurnoGravado();
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Cota);
        turno.TotalTokens.Should().Be(0);
    }

    [Fact]
    public async Task AbaixoDaCota_RespondeNormalmente()
    {
        LojaComCota(1_000);
        UsoDoMes(990);

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeTrue();
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task SemCota_NuncaBloqueiaNemSomaOMes()
    {
        LojaComCota(null);
        UsoDoMes(long.MaxValue / 2);

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeTrue();
        await _turnos.DidNotReceiveWithAnyArgs().SomarAsync(default, default, default, default);
    }

    [Fact]
    public async Task FerramentaDesligadaPelaLoja_NaoVaiAoModelo_EOPromptAvisa()
    {
        LojaComCota(null, "consultar_pedido");

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var requisicao = _requisicoes.Should().ContainSingle().Subject;
        requisicao.Ferramentas.Select(f => f.Nome).Should().NotContain("consultar_pedido");
        requisicao.TextoCompleto().Should().Contain("Desligadas pela loja: consultar_pedido.");
    }

    [Fact]
    public async Task PedidoDeFerramentaDesligada_VoltaErroSemExecutar()
    {
        LojaComCota(null, "consultar_pedido");
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(
            UsoFerramenta("consultar_pedido"), Texto("Vou verificar com a equipe."));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        await _consultarPedido.DidNotReceiveWithAnyArgs().ExecutarAsync(default!, default, default);
    }
}
