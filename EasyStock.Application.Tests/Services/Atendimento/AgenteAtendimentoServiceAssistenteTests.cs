using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// S68 (#2597): a configuração da loja chega ao turno. A escalada cita o nome da dona e o turno pede o modelo da loja
/// quando ele está na lista aprovada; fora dela, ou sem modelo, vale o padrão da infraestrutura (modelo nulo).
/// </summary>
public partial class AgenteAtendimentoServiceTests
{
    private readonly ModelosAprovadosAgenteOptions _modelos = ListaAprovada();

    private static ModelosAprovadosAgenteOptions ListaAprovada()
    {
        var lista = new ModelosAprovadosAgenteOptions();
        lista.Modelos.Add(new ModeloAprovadoAgente("claude-sonnet-5", "Padrão", "padrão"));
        lista.Modelos.Add(new ModeloAprovadoAgente("claude-haiku-5", "Econômico", "econômico"));
        return lista;
    }

    private ConfiguracaoAtendimento LojaLigada(string? nomeDona = null, string? modelo = null)
    {
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        configuracao.Ativo = true;
        configuracao.DefinirAssistente(nomeDona, modelo, null, null, null);
        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns(configuracao);
        return configuracao;
    }

    [Fact]
    public async Task EscaladaComOMoldePadraoCitaONomeDaDona()
    {
        _situacaoNoBanco = SituacaoConversa.Automatica;
        LojaLigada(nomeDona: "Michele");
        _escalador.When(e => e.EscalarAsync(_empresaId, _conversa, Arg.Any<string>(), Agora, Arg.Any<CancellationToken>()))
            .Do(_ => _conversa.Escalar(Agora, "quer falar com gente"));
        var entrada = JsonSerializer.SerializeToElement(new { motivo = "quer falar com gente" });
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(new RespostaLlm(RespostaLlm.StopToolUse,
            [new BlocoUsoFerramentaLlm("esc", "escalar_para_dona", entrada)], 1, 1));

        await CriarServico(new EscalarParaDonaFerramenta(_escalador)).ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        await _cloudClient.Received(1).EnviarTextoAsync(WaId,
            "Vou encaminhar sua mensagem para Michele conferir e responder por aqui.",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        _requisicoes[0].TextoCompleto().Should().Contain("a dona se chama Michele");
    }

    [Fact]
    public async Task TurnoUsaOModeloAprovadoDaLoja()
    {
        LojaLigada(modelo: "claude-haiku-5");

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        _requisicoes.Should().ContainSingle().Which.Modelo.Should().Be("claude-haiku-5");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("modelo-que-saiu-da-lista")]
    public async Task SemModeloOuForaDaListaUsaOPadrao(string? modelo)
    {
        LojaLigada(modelo: modelo);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        _requisicoes.Should().ContainSingle().Which.Modelo.Should().BeNull();
    }
}
