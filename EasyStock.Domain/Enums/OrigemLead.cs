namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Origem do lead capturado na landing publica. Define qual fluxo do site
    /// gerou o registro — guia priorizacao, copy do email de retorno e analytics.
    /// </summary>
    public enum OrigemLead
    {
        Newsletter = 0,
        FaleConosco = 1,
        TesteGratis = 2,
        AssineAgora = 3,
        WhatsApp = 4,
        Outro = 99
    }
}
