using System.Text.Json.Serialization;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// #2928: <c>campanha</c> opcional que o site manda ao abrir o chat e ao fechar o pedido (UTMs e ids de clique crus).
/// <see cref="Marketing"/> é o consentimento do site; sem ele o id de clique é descartado, mesmo que venha preenchido.
/// É o único ponto que converte o corpo do site em <see cref="OrigemAnuncio"/>.
/// </summary>
public sealed record CampanhaInput(
    [property: JsonPropertyName("utm_source")] string? UtmSource = null,
    [property: JsonPropertyName("utm_medium")] string? UtmMedium = null,
    [property: JsonPropertyName("utm_campaign")] string? UtmCampaign = null,
    [property: JsonPropertyName("utm_content")] string? UtmContent = null,
    [property: JsonPropertyName("utm_term")] string? UtmTerm = null,
    string? Gclid = null,
    string? Gbraid = null,
    string? Wbraid = null,
    string? Fbclid = null,
    bool Marketing = false)
{
    /// <summary>Nulo quando não sobra UTM nem id de clique útil.</summary>
    public OrigemAnuncio? ParaOrigem(string via, DateTimeOffset capturadaEm) =>
        OrigemAnuncio.DoSite(new OrigemAnuncio.Utms(UtmSource, UtmMedium, UtmCampaign, UtmContent, UtmTerm),
            new OrigemAnuncio.IdsDeClique(Gclid, Gbraid, Wbraid, Fbclid), via, Marketing, capturadaEm);

    /// <summary>Último anúncio vence (D2): a campanha do pedido, se útil; senão a que a conversa já tinha.</summary>
    public static OrigemAnuncio? Resolver(
        CampanhaInput? campanha, OrigemAnuncio? daConversa, string via, DateTimeOffset agora) =>
        campanha?.ParaOrigem(via, agora) ?? daConversa;
}
