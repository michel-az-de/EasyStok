using System.Globalization;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Mapeia respostas do Focus para os tipos de Result usados pelos use cases.
/// Convenções Focus (de docs):
///  - status="autorizado" → Autorizada.
///  - status="rejeitado" / "denegado" → Rejeitada/Denegada.
///  - status="processando_autorizacao" → tratar como Autorizada após webhook.
/// </summary>
public sealed class FocusNFeResponseMapper
{
    public ResultadoEmissaoNFCe Mapear(FocusNFeEmissaoResponse resp)
    {
        ArgumentNullException.ThrowIfNull(resp);

        var status = (resp.Status ?? "").ToLowerInvariant();
        var dh = ParseDateOrNull(resp.DataEmissao);

        return status switch
        {
            "autorizado" => new ResultadoEmissaoNFCe(
                ResultadoEmissao.Autorizada,
                resp.Protocolo,
                resp.Xml,
                dh,
                resp.CaminhoDanfe,
                resp.QrcodeUrl ?? resp.UrlConsultaNfe,
                Codigo: null,
                Motivo: null),

            "denegado" => new ResultadoEmissaoNFCe(
                ResultadoEmissao.Denegada,
                Protocolo: null,
                XmlAutorizado: null,
                DhAutorizacao: null,
                UrlDanfeFiscal: null,
                UrlConsultaQr: null,
                Codigo: ExtractCodigo(resp),
                Motivo: ExtractMotivo(resp)),

            _ => new ResultadoEmissaoNFCe(
                ResultadoEmissao.Rejeitada,
                Protocolo: null,
                XmlAutorizado: null,
                DhAutorizacao: null,
                UrlDanfeFiscal: null,
                UrlConsultaQr: null,
                Codigo: ExtractCodigo(resp),
                Motivo: ExtractMotivo(resp)),
        };
    }

    public ResultadoCancelamentoNFCe MapearCancelamento(FocusNFeCancelamentoResponse resp)
    {
        ArgumentNullException.ThrowIfNull(resp);
        var status = (resp.Status ?? "").ToLowerInvariant();
        var dh = ParseDateOrNull(resp.DataEvento);
        var sucesso = status == "cancelado";
        return new ResultadoCancelamentoNFCe(
            Sucesso: sucesso,
            Protocolo: sucesso ? resp.Protocolo : null,
            XmlEvento: sucesso ? resp.Xml : null,
            DhCancelamento: sucesso ? dh : null,
            Codigo: sucesso ? null : resp.StatusSefaz,
            Motivo: sucesso ? null : resp.MensagemSefaz);
    }

    public ResultadoInutilizacaoNFCe MapearInutilizacao(FocusNFeInutilizacaoResponse resp)
    {
        ArgumentNullException.ThrowIfNull(resp);
        var status = (resp.Status ?? "").ToLowerInvariant();
        var sucesso = status is "autorizado" or "inutilizado";
        return new ResultadoInutilizacaoNFCe(
            Sucesso: sucesso,
            Protocolo: sucesso ? resp.Protocolo : null,
            XmlEvento: sucesso ? resp.Xml : null,
            Codigo: sucesso ? null : resp.StatusSefaz,
            Motivo: sucesso ? null : resp.MensagemSefaz);
    }

    private static string? ExtractCodigo(FocusNFeEmissaoResponse resp)
    {
        if (!string.IsNullOrWhiteSpace(resp.StatusSefaz)) return resp.StatusSefaz;
        if (!string.IsNullOrWhiteSpace(resp.Codigo)) return resp.Codigo;
        var primeiro = resp.Erros?.FirstOrDefault();
        return primeiro?.Codigo;
    }

    private static string? ExtractMotivo(FocusNFeEmissaoResponse resp)
    {
        if (!string.IsNullOrWhiteSpace(resp.MensagemSefaz)) return resp.MensagemSefaz;
        if (!string.IsNullOrWhiteSpace(resp.Mensagem)) return resp.Mensagem;
        var primeiro = resp.Erros?.FirstOrDefault();
        return primeiro?.Mensagem;
    }

    private static DateTime? ParseDateOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToUniversalTime();
        return null;
    }
}
