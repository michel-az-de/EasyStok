namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Configuracao do adapter Focus NFe. Bind no appsettings.json em <c>FocusNFe</c>.
///
/// <para>
/// Token e CSC sao por tenant — vivem em <c>CredencialIntegracao</c> cifrada,
/// nao aqui. Esta classe e apenas para URL/timeout/comportamento global.
/// </para>
/// </summary>
public sealed class FocusNFeOptions
{
    public const string SectionName = "FocusNFe";

    /// <summary>URL base. Sandbox: <c>https://homologacao.focusnfe.com.br/v2/</c>. Producao: <c>https://api.focusnfe.com.br/v2/</c>.</summary>
    public string BaseUrl { get; set; } = "https://homologacao.focusnfe.com.br/v2/";

    /// <summary>Timeout total da chamada HTTP (default 30s).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Secret HMAC para validar assinatura do webhook (header <c>X-Focus-Signature</c>).
    /// Configurado uma vez no painel Focus por ambiente. Sandbox + Producao podem ter secrets distintos.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Quando <c>true</c>, o adapter loga payload completo (apenas em desenvolvimento — pode vazar dados fiscais).</summary>
    public bool LogPayloadCompleto { get; set; }

    /// <summary>
    /// Indicador de presença do consumidor — 1=presencial, 2=internet, 3=teleatendimento,
    /// 4=NFC-e entrega domicílio, 9=outros. PWA Caixa padrão é 1 (presencial).
    /// </summary>
    public int PresencaCompradorPadrao { get; set; } = 1;

    /// <summary>
    /// Modalidade de frete padrão — 0=por conta do emitente, 1=destinatário, 2=terceiros,
    /// 3=transporte próprio remetente, 4=transporte próprio destinatário, 9=sem frete.
    /// NFC-e típica é 9 (sem frete — venda balcão).
    /// </summary>
    public int ModalidadeFretePadrao { get; set; } = 9;

    /// <summary>
    /// Forma de pagamento padrão quando o caller não informa explicitamente.
    /// "01"=dinheiro, "02"=cheque, "03"=cartão crédito, "04"=cartão débito,
    /// "05"=crédito loja, "10"=vale alimentação, "11"=vale refeição, "12"=vale presente,
    /// "13"=vale combustível, "15"=boleto, "17"=Pix, "99"=outros.
    /// </summary>
    public string FormaPagamentoPadrao { get; set; } = "01";
}
