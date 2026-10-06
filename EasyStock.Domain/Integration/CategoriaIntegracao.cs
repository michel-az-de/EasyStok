namespace EasyStock.Domain.Integration;

/// <summary>
/// Categoria de integração externa. Usada como discriminador na tabela
/// <c>credencial_integracao</c> e como chave dos pipelines de resiliência
/// Polly (ver <c>EasyStock.Infra.Integrations.Resilience.IntegrationCategories</c>).
/// </summary>
public enum CategoriaIntegracao
{
    /// <summary>Gateways de pagamento (Mercado Pago, PicPay, Pix Efí).</summary>
    Payments = 1,

    /// <summary>Emissão fiscal (NFe SEFAZ direto, SaaS terceirizado).</summary>
    Fiscal = 2,

    /// <summary>Marketplaces (Mercado Livre, iFood).</summary>
    Marketplace = 3,

    /// <summary>Logística e transportadoras (99Entrega, Correios).</summary>
    Logistics = 4,

    /// <summary>
    /// Canais de mensagem (business token do WhatsApp da empresa, #1417). A coluna guarda o inteiro e não tem
    /// check constraint: valor novo não pede migration.
    /// </summary>
    Mensageria = 5,
}
