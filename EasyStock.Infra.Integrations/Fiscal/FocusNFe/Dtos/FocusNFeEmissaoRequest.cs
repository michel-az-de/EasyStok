using System.Text.Json.Serialization;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;

/// <summary>
/// Payload JSON enviado ao endpoint POST /nfce do Focus NFe. Estrutura segue
/// layout SEFAZ NFC-e 4.00, mapeado pelo padrao Focus. Campos marcados nullable
/// sao opcionais conforme regime tributario.
/// </summary>
public sealed class FocusNFeEmissaoRequest
{
    [JsonPropertyName("natureza_operacao")]
    public string NaturezaOperacao { get; set; } = "Venda ao consumidor";

    [JsonPropertyName("data_emissao")]
    public string DataEmissao { get; set; } = null!; // ISO 8601

    [JsonPropertyName("tipo_documento")]
    public int TipoDocumento { get; set; } = 1; // 1=saida

    [JsonPropertyName("finalidade_emissao")]
    public int FinalidadeEmissao { get; set; } = 1; // 1=normal

    [JsonPropertyName("consumidor_final")]
    public int ConsumidorFinal { get; set; } = 1;

    [JsonPropertyName("presenca_comprador")]
    public int PresencaComprador { get; set; } = 1; // 1=presencial

    [JsonPropertyName("cnpj_emitente")]
    public string CnpjEmitente { get; set; } = null!;

    [JsonPropertyName("nome_emitente")]
    public string? NomeEmitente { get; set; }

    [JsonPropertyName("nome_fantasia_emitente")]
    public string? NomeFantasiaEmitente { get; set; }

    [JsonPropertyName("logradouro_emitente")]
    public string? LogradouroEmitente { get; set; }

    [JsonPropertyName("numero_emitente")]
    public string? NumeroEmitente { get; set; }

    [JsonPropertyName("bairro_emitente")]
    public string? BairroEmitente { get; set; }

    [JsonPropertyName("municipio_emitente")]
    public string? MunicipioEmitente { get; set; }

    [JsonPropertyName("uf_emitente")]
    public string? UfEmitente { get; set; }

    [JsonPropertyName("cep_emitente")]
    public string? CepEmitente { get; set; }

    [JsonPropertyName("inscricao_estadual_emitente")]
    public string? InscricaoEstadualEmitente { get; set; }

    [JsonPropertyName("regime_tributario_emitente")]
    public int RegimeTributarioEmitente { get; set; } // 1=simples, 3=normal

    [JsonPropertyName("cpf_destinatario")]
    public string? CpfDestinatario { get; set; }

    [JsonPropertyName("nome_destinatario")]
    public string? NomeDestinatario { get; set; }

    [JsonPropertyName("modalidade_frete")]
    public int ModalidadeFrete { get; set; } = 9; // 9=sem frete

    [JsonPropertyName("valor_total")]
    public decimal ValorTotal { get; set; }

    [JsonPropertyName("valor_produtos")]
    public decimal ValorProdutos { get; set; }

    [JsonPropertyName("items")]
    public List<FocusNFeItem> Items { get; set; } = new();

    [JsonPropertyName("formas_pagamento")]
    public List<FocusNFePagamento> FormasPagamento { get; set; } = new();
}

public sealed class FocusNFeItem
{
    [JsonPropertyName("numero_item")]
    public int NumeroItem { get; set; }

    [JsonPropertyName("codigo_produto")]
    public string CodigoProduto { get; set; } = null!;

    [JsonPropertyName("descricao")]
    public string Descricao { get; set; } = null!;

    [JsonPropertyName("codigo_ncm")]
    public string? CodigoNcm { get; set; }

    [JsonPropertyName("cfop")]
    public string? Cfop { get; set; }

    [JsonPropertyName("unidade_comercial")]
    public string Unidade { get; set; } = "UN";

    [JsonPropertyName("quantidade_comercial")]
    public decimal Quantidade { get; set; }

    [JsonPropertyName("valor_unitario_comercial")]
    public decimal ValorUnitario { get; set; }

    [JsonPropertyName("valor_bruto")]
    public decimal ValorBruto { get; set; }

    [JsonPropertyName("unidade_tributavel")]
    public string UnidadeTributavel { get; set; } = "UN";

    [JsonPropertyName("quantidade_tributavel")]
    public decimal QuantidadeTributavel { get; set; }

    [JsonPropertyName("valor_unitario_tributavel")]
    public decimal ValorUnitarioTributavel { get; set; }

    [JsonPropertyName("origem_mercadoria")]
    public byte OrigemMercadoria { get; set; }

    [JsonPropertyName("icms_situacao_tributaria")]
    public string? IcmsSituacaoTributaria { get; set; }

    [JsonPropertyName("pis_situacao_tributaria")]
    public string? PisSituacaoTributaria { get; set; } = "07";

    [JsonPropertyName("cofins_situacao_tributaria")]
    public string? CofinsSituacaoTributaria { get; set; } = "07";
}

public sealed class FocusNFePagamento
{
    [JsonPropertyName("forma_pagamento")]
    public string FormaPagamento { get; set; } = "01"; // 01=dinheiro

    [JsonPropertyName("valor_pagamento")]
    public decimal ValorPagamento { get; set; }
}
