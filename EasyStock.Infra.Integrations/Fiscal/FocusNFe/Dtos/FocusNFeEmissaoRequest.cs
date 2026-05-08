using System.Text.Json.Serialization;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;

/// <summary>
/// Payload JSON enviado ao Focus NFe no POST /v2/nfce. Mantém PT-BR
/// snake_case (convenção da API Focus). Ver doc oficial em
/// https://focusnfe.com.br/doc/.
/// </summary>
public sealed class FocusNFeEmissaoRequest
{
    [JsonPropertyName("natureza_operacao")]
    public string NaturezaOperacao { get; set; } = "Venda de mercadoria";

    [JsonPropertyName("data_emissao")]
    public string DataEmissao { get; set; } = "";

    [JsonPropertyName("tipo_documento")]
    public int TipoDocumento { get; set; } = 1;

    [JsonPropertyName("finalidade_emissao")]
    public int FinalidadeEmissao { get; set; } = 1;

    [JsonPropertyName("presenca_comprador")]
    public int PresencaComprador { get; set; } = 1;

    [JsonPropertyName("consumidor_final")]
    public int ConsumidorFinal { get; set; } = 1;

    [JsonPropertyName("modalidade_frete")]
    public int ModalidadeFrete { get; set; } = 9;

    [JsonPropertyName("ambiente")]
    public string Ambiente { get; set; } = "homologacao";

    [JsonPropertyName("cnpj_emitente")]
    public string CnpjEmitente { get; set; } = "";

    [JsonPropertyName("nome_emitente")]
    public string NomeEmitente { get; set; } = "";

    [JsonPropertyName("logradouro_emitente")]
    public string LogradouroEmitente { get; set; } = "";

    [JsonPropertyName("numero_emitente")]
    public string NumeroEmitente { get; set; } = "";

    [JsonPropertyName("complemento_emitente")]
    public string? ComplementoEmitente { get; set; }

    [JsonPropertyName("bairro_emitente")]
    public string BairroEmitente { get; set; } = "";

    [JsonPropertyName("municipio_emitente")]
    public string MunicipioEmitente { get; set; } = "";

    [JsonPropertyName("uf_emitente")]
    public string UfEmitente { get; set; } = "";

    [JsonPropertyName("cep_emitente")]
    public string CepEmitente { get; set; } = "";

    [JsonPropertyName("inscricao_estadual_emitente")]
    public string InscricaoEstadualEmitente { get; set; } = "";

    [JsonPropertyName("regime_tributario_emitente")]
    public int RegimeTributarioEmitente { get; set; } = 1;

    [JsonPropertyName("cpf_destinatario")]
    public string? CpfDestinatario { get; set; }

    [JsonPropertyName("cnpj_destinatario")]
    public string? CnpjDestinatario { get; set; }

    [JsonPropertyName("nome_destinatario")]
    public string? NomeDestinatario { get; set; }

    [JsonPropertyName("serie")]
    public int Serie { get; set; }

    [JsonPropertyName("numero")]
    public int Numero { get; set; }

    [JsonPropertyName("forma_emissao")]
    public int FormaEmissao { get; set; } = 1;

    [JsonPropertyName("items")]
    public List<FocusNFeItemRequest> Items { get; set; } = new();

    [JsonPropertyName("formas_pagamento")]
    public List<FocusNFePagamentoRequest> FormasPagamento { get; set; } = new();

    [JsonPropertyName("valor_total")]
    public decimal ValorTotal { get; set; }

    [JsonPropertyName("valor_produtos")]
    public decimal ValorProdutos { get; set; }

    [JsonPropertyName("informacoes_adicionais_contribuinte")]
    public string? InformacoesAdicionaisContribuinte { get; set; }
}

public sealed class FocusNFeItemRequest
{
    [JsonPropertyName("numero_item")] public int NumeroItem { get; set; }
    [JsonPropertyName("codigo_produto")] public string CodigoProduto { get; set; } = "";
    [JsonPropertyName("descricao")] public string Descricao { get; set; } = "";
    [JsonPropertyName("ean")] public string? Ean { get; set; }
    [JsonPropertyName("cfop")] public string Cfop { get; set; } = "";
    [JsonPropertyName("ncm")] public string Ncm { get; set; } = "";
    [JsonPropertyName("cest")] public string? Cest { get; set; }
    [JsonPropertyName("unidade_comercial")] public string UnidadeComercial { get; set; } = "";
    [JsonPropertyName("quantidade_comercial")] public decimal QuantidadeComercial { get; set; }
    [JsonPropertyName("valor_unitario_comercial")] public decimal ValorUnitarioComercial { get; set; }
    [JsonPropertyName("valor_bruto")] public decimal ValorBruto { get; set; }
    [JsonPropertyName("valor_desconto")] public decimal ValorDesconto { get; set; }
    [JsonPropertyName("unidade_tributavel")] public string UnidadeTributavel { get; set; } = "";
    [JsonPropertyName("quantidade_tributavel")] public decimal QuantidadeTributavel { get; set; }
    [JsonPropertyName("valor_unitario_tributavel")] public decimal ValorUnitarioTributavel { get; set; }
    [JsonPropertyName("inclui_no_total")] public int IncluiNoTotal { get; set; } = 1;

    [JsonPropertyName("icms_origem")] public int IcmsOrigem { get; set; }
    [JsonPropertyName("icms_situacao_tributaria")] public string IcmsSituacaoTributaria { get; set; } = "";
    [JsonPropertyName("icms_aliquota")] public decimal? IcmsAliquota { get; set; }
    [JsonPropertyName("icms_valor")] public decimal? IcmsValor { get; set; }

    [JsonPropertyName("pis_situacao_tributaria")] public string PisSituacaoTributaria { get; set; } = "";
    [JsonPropertyName("pis_aliquota_porcentual")] public decimal? PisAliquota { get; set; }
    [JsonPropertyName("pis_valor")] public decimal? PisValor { get; set; }

    [JsonPropertyName("cofins_situacao_tributaria")] public string CofinsSituacaoTributaria { get; set; } = "";
    [JsonPropertyName("cofins_aliquota_porcentual")] public decimal? CofinsAliquota { get; set; }
    [JsonPropertyName("cofins_valor")] public decimal? CofinsValor { get; set; }
}

public sealed class FocusNFePagamentoRequest
{
    [JsonPropertyName("forma_pagamento")] public string FormaPagamento { get; set; } = "";
    [JsonPropertyName("valor_pagamento")] public decimal ValorPagamento { get; set; }
    [JsonPropertyName("bandeira_operadora")] public string? BandeiraOperadora { get; set; }
    [JsonPropertyName("cnpj_credenciadora")] public string? CnpjCredenciadora { get; set; }
    [JsonPropertyName("nsu")] public string? Nsu { get; set; }
}
