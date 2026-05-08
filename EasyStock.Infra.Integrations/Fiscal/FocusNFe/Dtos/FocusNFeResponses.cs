using System.Text.Json.Serialization;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;

public sealed class FocusNFeEmissaoResponse
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("status_sefaz")] public string? StatusSefaz { get; set; }
    [JsonPropertyName("mensagem_sefaz")] public string? MensagemSefaz { get; set; }
    [JsonPropertyName("chave_nfe")] public string? ChaveNFe { get; set; }
    [JsonPropertyName("numero")] public int? Numero { get; set; }
    [JsonPropertyName("serie")] public string? Serie { get; set; }
    [JsonPropertyName("protocolo")] public string? Protocolo { get; set; }
    [JsonPropertyName("data_emissao")] public string? DataEmissao { get; set; }
    [JsonPropertyName("ref")] public string? Ref { get; set; }
    [JsonPropertyName("caminho_xml_nota_fiscal")] public string? CaminhoXmlNotaFiscal { get; set; }
    [JsonPropertyName("caminho_danfe")] public string? CaminhoDanfe { get; set; }
    [JsonPropertyName("url_consulta_nfe")] public string? UrlConsultaNfe { get; set; }
    [JsonPropertyName("qrcode_url")] public string? QrcodeUrl { get; set; }
    [JsonPropertyName("xml")] public string? Xml { get; set; }
    [JsonPropertyName("erros")] public List<FocusNFeErroDto>? Erros { get; set; }
    [JsonPropertyName("codigo")] public string? Codigo { get; set; }
    [JsonPropertyName("mensagem")] public string? Mensagem { get; set; }
}

public sealed class FocusNFeErroDto
{
    [JsonPropertyName("codigo")] public string? Codigo { get; set; }
    [JsonPropertyName("mensagem")] public string? Mensagem { get; set; }
}

public sealed class FocusNFeCancelamentoResponse
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("status_sefaz")] public string? StatusSefaz { get; set; }
    [JsonPropertyName("mensagem_sefaz")] public string? MensagemSefaz { get; set; }
    [JsonPropertyName("protocolo")] public string? Protocolo { get; set; }
    [JsonPropertyName("data_evento")] public string? DataEvento { get; set; }
    [JsonPropertyName("xml")] public string? Xml { get; set; }
    [JsonPropertyName("caminho_xml")] public string? CaminhoXml { get; set; }
}

public sealed class FocusNFeInutilizacaoResponse
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("status_sefaz")] public string? StatusSefaz { get; set; }
    [JsonPropertyName("mensagem_sefaz")] public string? MensagemSefaz { get; set; }
    [JsonPropertyName("protocolo")] public string? Protocolo { get; set; }
    [JsonPropertyName("xml")] public string? Xml { get; set; }
}

/// <summary>
/// Payload do webhook do Focus. Recebido em /api/webhooks/focus-nfe com
/// header X-Focus-Signature (HMAC-SHA256 base64).
/// </summary>
public sealed class FocusNFeWebhookPayload
{
    [JsonPropertyName("ref")] public string? Ref { get; set; }
    [JsonPropertyName("chave_nfe")] public string? ChaveNFe { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("protocolo")] public string? Protocolo { get; set; }
    [JsonPropertyName("data_evento")] public string? DataEvento { get; set; }
    [JsonPropertyName("xml")] public string? Xml { get; set; }
    [JsonPropertyName("motivo")] public string? Motivo { get; set; }
    [JsonPropertyName("codigo")] public string? Codigo { get; set; }
    [JsonPropertyName("mensagem_sefaz")] public string? MensagemSefaz { get; set; }
}
