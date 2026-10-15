using EasyStock.Domain.Integration;

namespace EasyStock.Application.UseCases.Integracoes;

/// <summary>
/// Uma integração da tela de Integrações (F16, #1246).
/// </summary>
/// <param name="Provider">Chave estável (rota da API e <c>provider_key</c> no banco).</param>
/// <param name="LojaGrava">A loja pode salvar a própria chave. Falso = tudo é da FMA (WhatsApp).</param>
/// <param name="CamposObrigatorios">Campos da chave da loja; todos são segredo e nunca voltam pela API.</param>
/// <param name="CampoMascara">Campo cujos últimos 4 caracteres viram a máscara.</param>
/// <param name="EscolheAmbiente">A loja escolhe sandbox ou produção (Lalamove). Os outros são sempre produção.</param>
/// <param name="GeridoPelaFma">O que é global da app e aparece só como leitura.</param>
public sealed record DefinicaoIntegracao(
    string Provider,
    string Nome,
    CategoriaIntegracao Categoria,
    bool LojaGrava,
    IReadOnlyList<string> CamposObrigatorios,
    string? CampoMascara,
    bool EscolheAmbiente,
    string? GeridoPelaFma);

/// <summary>
/// As integrações que a loja vê e testa (F16). WhatsApp: o número decide para qual empresa o
/// webhook entrega, então só a FMA vincula (<c>VincularWhatsAppDoTenantUseCase</c>); a loja só
/// testa. Mercado Pago: o webhook secret é da app (global).
/// </summary>
public static class CatalogoIntegracoes
{
    public const string MercadoPago = "mercadopago";
    public const string WhatsApp = "whatsapp";
    public const string GoogleMaps = "googlemaps";
    public const string Lalamove = "lalamove";

    public const string CampoAccessToken = "accessToken";
    public const string CampoApiKey = "apiKey";
    public const string CampoApiSecret = "apiSecret";
    public const string CampoPhoneNumberId = "phoneNumberId";

    public static readonly IReadOnlyList<DefinicaoIntegracao> Todas =
    [
        new(MercadoPago, "Mercado Pago", CategoriaIntegracao.Payments, LojaGrava: true,
            [CampoAccessToken], CampoAccessToken, EscolheAmbiente: false,
            GeridoPelaFma: "O segredo do webhook é da aplicação da FMA."),
        new(WhatsApp, "WhatsApp", CategoriaIntegracao.Mensageria, LojaGrava: false,
            [], null, EscolheAmbiente: false,
            GeridoPelaFma: "Token, App Secret, Verify Token e o número da loja são geridos pela FMA."),
        new(GoogleMaps, "Google Maps", CategoriaIntegracao.Mapas, LojaGrava: true,
            [CampoApiKey], CampoApiKey, EscolheAmbiente: false, GeridoPelaFma: null),
        new(Lalamove, "Lalamove", CategoriaIntegracao.Logistics, LojaGrava: true,
            [CampoApiKey, CampoApiSecret], CampoApiKey, EscolheAmbiente: true, GeridoPelaFma: null),
    ];

    public static DefinicaoIntegracao? Obter(string? provider) =>
        Todas.FirstOrDefault(d => string.Equals(d.Provider, provider?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary><c>sandbox</c> ou <c>producao</c>, como a API fala.</summary>
    public static string NomeAmbiente(AmbienteIntegracao ambiente) =>
        ambiente == AmbienteIntegracao.Sandbox ? "sandbox" : "producao";

    public static AmbienteIntegracao? LerAmbiente(string? valor) => valor?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "sandbox" => AmbienteIntegracao.Sandbox,
        "producao" or "produção" or "production" => AmbienteIntegracao.Production,
        _ => throw new UseCaseValidationException("Ambiente inválido: use sandbox ou producao."),
    };

    /// <summary>Últimos 4 caracteres do segredo; segredo curto demais não ganha máscara.</summary>
    public static string? Mascara(string? segredo)
    {
        var limpo = segredo?.Trim();
        return limpo is { Length: >= 8 } ? limpo[^CredencialIntegracao.MascaraTamanhoMaximo..] : null;
    }
}
