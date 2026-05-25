using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Mapeia respostas do Focus NFe para os DTOs <see cref="ResultadoEmissaoNfce"/>
/// ou lanca <see cref="GatewayFiscalException"/> tipadas para o use case interpretar.
/// </summary>
public static class FocusNFeResponseMapper
{
    /// <summary>
    /// Interpreta a resposta da emissao. Status "autorizado" -> sucesso.
    /// Status "processando_autorizacao" -> caller deve aguardar webhook (lanca TransienteException
    /// para colocar a nota em FalhaTransiente; webhook depois moves para Autorizada).
    /// Outros status -> rejeicao.
    /// </summary>
    public static ResultadoEmissaoNfce MapEmissao(FocusNFeEmissaoResponse resp)
    {
        var status = (resp.Status ?? string.Empty).ToLowerInvariant();

        if (status is "autorizado")
        {
            if (string.IsNullOrWhiteSpace(resp.ChaveNfe) || string.IsNullOrWhiteSpace(resp.Protocolo))
                throw new GatewayFiscalTransienteException("Focus retornou status=autorizado mas sem chave_nfe/protocolo.");

            return new ResultadoEmissaoNfce(
                ChaveAcesso: resp.ChaveNfe,
                ProtocoloAutorizacao: resp.Protocolo,
                DataAutorizacao: resp.DataEmissao ?? DateTime.UtcNow,
                XmlAssinadoUrl: resp.CaminhoXmlNotaFiscal,
                DanfeUrl: resp.CaminhoDanfe);
        }

        if (status is "processando_autorizacao")
        {
            throw new GatewayFiscalTransienteException(
                "Focus aceitou mas SEFAZ ainda processando. Webhook ira completar.");
        }

        if (status is "denegado")
        {
            throw new GatewayFiscalDenegadaException(
                resp.MensagemSefaz ?? resp.Mensagem ?? "Denegada pela SEFAZ.");
        }

        // erro_autorizacao, cancelado, etc — tratado como rejeicao
        var motivo = resp.MensagemSefaz ?? resp.Mensagem ?? $"Status nao reconhecido: {resp.Status}";
        var codigo = resp.StatusSefaz ?? resp.Codigo;
        throw new GatewayFiscalRejeitadaException(motivo, codigo);
    }

    public static ResultadoCancelamentoNfce MapCancelamento(FocusNFeCancelamentoResponse resp)
    {
        var status = (resp.Status ?? string.Empty).ToLowerInvariant();

        if (status is "cancelado")
        {
            if (string.IsNullOrWhiteSpace(resp.ProtocoloCancelamento))
                throw new GatewayFiscalTransienteException("Focus cancelamento sem protocolo.");

            return new ResultadoCancelamentoNfce(
                ProtocoloEvento: resp.ProtocoloCancelamento,
                DataCancelamento: resp.DataCancelamento ?? DateTime.UtcNow);
        }

        throw new GatewayFiscalRejeitadaException(
            resp.MensagemSefaz ?? $"Cancelamento nao confirmado: {resp.Status}",
            resp.StatusSefaz);
    }

    public static ResultadoInutilizacaoNfce MapInutilizacao(FocusNFeInutilizacaoResponse resp)
    {
        var status = (resp.Status ?? string.Empty).ToLowerInvariant();

        if (status is "inutilizado" or "homologado")
        {
            if (string.IsNullOrWhiteSpace(resp.Protocolo))
                throw new GatewayFiscalTransienteException("Focus inutilizacao sem protocolo.");

            return new ResultadoInutilizacaoNfce(
                ProtocoloEvento: resp.Protocolo,
                DataInutilizacao: resp.DataInutilizacao ?? DateTime.UtcNow);
        }

        throw new GatewayFiscalRejeitadaException(
            resp.MensagemSefaz ?? $"Inutilizacao nao confirmada: {resp.Status}",
            resp.StatusSefaz);
    }

    public static ResultadoConsultaNfce MapConsulta(FocusNFeEmissaoResponse resp)
    {
        return new ResultadoConsultaNfce(
            StatusGateway: resp.Status ?? "desconhecido",
            ProtocoloAutorizacao: resp.Protocolo,
            MotivoRejeicao: resp.MensagemSefaz ?? resp.Mensagem,
            DataAutorizacao: resp.DataEmissao,
            XmlAssinadoUrl: resp.CaminhoXmlNotaFiscal,
            DanfeUrl: resp.CaminhoDanfe);
    }
}
