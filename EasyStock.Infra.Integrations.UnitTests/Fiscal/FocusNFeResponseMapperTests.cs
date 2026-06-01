using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;
using FluentAssertions;

namespace EasyStock.Infra.Integrations.UnitTests.Fiscal;

/// <summary>
/// Spec-tests do <see cref="FocusNFeResponseMapper"/> — Track A1, refs #274.
/// Oráculo = o contrato <see cref="IGatewayFiscal"/>: cada status de resposta da
/// Focus DEVE virar o resultado ou a exceção tipada correta, para o use case marcar
/// Autorizada / Rejeitada / Denegada / FalhaTransiente sem ambiguidade. Mapeamento
/// errado aqui = nota fiscal em estado errado, então é o caminho que mais importa.
/// </summary>
public class FocusNFeResponseMapperTests
{
    // ---------- MapEmissao ----------

    [Fact]
    public void MapEmissao_autorizado_completo_retorna_resultado()
    {
        var resp = new FocusNFeEmissaoResponse
        {
            Status = "autorizado",
            ChaveNfe = "35200000000000000000000000000000000000000000",
            Protocolo = "135200000000123",
            CaminhoXmlNotaFiscal = "/xml/abc",
            CaminhoDanfe = "/danfe/abc",
        };

        var r = FocusNFeResponseMapper.MapEmissao(resp);

        r.ChaveAcesso.Should().Be(resp.ChaveNfe);
        r.ProtocoloAutorizacao.Should().Be("135200000000123");
        r.XmlAssinadoUrl.Should().Be("/xml/abc");
        r.DanfeUrl.Should().Be("/danfe/abc");
    }

    [Fact]
    public void MapEmissao_autorizado_sem_chave_e_transiente()
    {
        // "autorizado" sem chave/protocolo é resposta inconsistente → transiente
        // (reprocessa via contingência), NUNCA sucesso silencioso.
        var resp = new FocusNFeEmissaoResponse { Status = "autorizado", Protocolo = "x" };

        var act = () => FocusNFeResponseMapper.MapEmissao(resp);

        act.Should().Throw<GatewayFiscalTransienteException>();
    }

    [Fact]
    public void MapEmissao_processando_autorizacao_e_transiente()
    {
        // Focus aceitou mas SEFAZ ainda processa → transiente; webhook completa depois.
        var resp = new FocusNFeEmissaoResponse { Status = "processando_autorizacao" };

        var act = () => FocusNFeResponseMapper.MapEmissao(resp);

        act.Should().Throw<GatewayFiscalTransienteException>();
    }

    [Fact]
    public void MapEmissao_denegado_e_denegada()
    {
        var resp = new FocusNFeEmissaoResponse { Status = "denegado", MensagemSefaz = "Contribuinte irregular" };

        var act = () => FocusNFeResponseMapper.MapEmissao(resp);

        act.Should().Throw<GatewayFiscalDenegadaException>()
            .Which.Motivo.Should().Be("Contribuinte irregular");
    }

    [Fact]
    public void MapEmissao_erro_autorizacao_e_rejeitada_com_codigo_e_motivo()
    {
        var resp = new FocusNFeEmissaoResponse
        {
            Status = "erro_autorizacao",
            StatusSefaz = "539",
            MensagemSefaz = "Duplicidade de NF-e",
        };

        var act = () => FocusNFeResponseMapper.MapEmissao(resp);
        var ex = act.Should().Throw<GatewayFiscalRejeitadaException>().Which;

        ex.Codigo.Should().Be("539");
        ex.Motivo.Should().Be("Duplicidade de NF-e");
    }

    // ---------- MapCancelamento ----------

    [Fact]
    public void MapCancelamento_cancelado_retorna_resultado()
    {
        var resp = new FocusNFeCancelamentoResponse { Status = "cancelado", ProtocoloCancelamento = "p-cancel-1" };

        var r = FocusNFeResponseMapper.MapCancelamento(resp);

        r.ProtocoloEvento.Should().Be("p-cancel-1");
    }

    [Fact]
    public void MapCancelamento_nao_confirmado_e_rejeitada()
    {
        var resp = new FocusNFeCancelamentoResponse { Status = "erro", MensagemSefaz = "Fora do prazo de cancelamento" };

        var act = () => FocusNFeResponseMapper.MapCancelamento(resp);

        act.Should().Throw<GatewayFiscalRejeitadaException>();
    }

    // ---------- MapInutilizacao ----------

    [Fact]
    public void MapInutilizacao_inutilizado_retorna_resultado()
    {
        var resp = new FocusNFeInutilizacaoResponse { Status = "inutilizado", Protocolo = "p-inut-1" };

        var r = FocusNFeResponseMapper.MapInutilizacao(resp);

        r.ProtocoloEvento.Should().Be("p-inut-1");
    }

    [Fact]
    public void MapInutilizacao_nao_confirmada_e_rejeitada()
    {
        var resp = new FocusNFeInutilizacaoResponse { Status = "erro" };

        var act = () => FocusNFeResponseMapper.MapInutilizacao(resp);

        act.Should().Throw<GatewayFiscalRejeitadaException>();
    }
}
