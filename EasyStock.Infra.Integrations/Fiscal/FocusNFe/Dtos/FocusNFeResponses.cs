using System.Text.Json.Serialization;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;

/// <summary>
/// Resposta sincrona da API Focus NFe. <see cref="Status"/> e o caminho feliz: "autorizado"
/// significa SEFAZ retornou autorizacao na mesma chamada. Em "processando_autorizacao",
/// Focus ja recebeu mas SEFAZ ainda nao respondeu — cliente deve aguardar webhook.
/// </summary>
public sealed class FocusNFeEmissaoResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("status_sefaz")]
    public string? StatusSefaz { get; set; }

    [JsonPropertyName("mensagem_sefaz")]
    public string? MensagemSefaz { get; set; }

    [JsonPropertyName("chave_nfe")]
    public string? ChaveNfe { get; set; }

    [JsonPropertyName("protocolo")]
    public string? Protocolo { get; set; }

    [JsonPropertyName("data_emissao")]
    public DateTime? DataEmissao { get; set; }

    [JsonPropertyName("caminho_xml_nota_fiscal")]
    public string? CaminhoXmlNotaFiscal { get; set; }

    [JsonPropertyName("caminho_danfe")]
    public string? CaminhoDanfe { get; set; }

    [JsonPropertyName("codigo")]
    public string? Codigo { get; set; }

    [JsonPropertyName("mensagem")]
    public string? Mensagem { get; set; }

    [JsonPropertyName("erros")]
    public List<FocusNFeErro>? Erros { get; set; }
}

public sealed class FocusNFeErro
{
    [JsonPropertyName("codigo")]
    public string? Codigo { get; set; }

    [JsonPropertyName("mensagem")]
    public string? Mensagem { get; set; }
}

public sealed class FocusNFeCancelamentoResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("status_sefaz")]
    public string? StatusSefaz { get; set; }

    [JsonPropertyName("mensagem_sefaz")]
    public string? MensagemSefaz { get; set; }

    [JsonPropertyName("protocolo_cancelamento")]
    public string? ProtocoloCancelamento { get; set; }

    [JsonPropertyName("data_cancelamento")]
    public DateTime? DataCancelamento { get; set; }
}

public sealed class FocusNFeInutilizacaoResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("status_sefaz")]
    public string? StatusSefaz { get; set; }

    [JsonPropertyName("mensagem_sefaz")]
    public string? MensagemSefaz { get; set; }

    [JsonPropertyName("protocolo")]
    public string? Protocolo { get; set; }

    [JsonPropertyName("data_inutilizacao")]
    public DateTime? DataInutilizacao { get; set; }
}
