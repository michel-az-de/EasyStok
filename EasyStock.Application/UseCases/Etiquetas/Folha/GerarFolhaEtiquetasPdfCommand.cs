using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Application.UseCases.Etiquetas.Folha;

/// <summary>
/// Pedido da folha A4 de etiquetas em PDF (#2200). Cada etiqueta já vem com o LayoutJson v1 resolvido: só texto
/// pronto, sem <c>{variável}</c>. <paramref name="Pular"/> deixa vazias as primeiras posições da primeira folha,
/// para reaproveitar uma folha Pimaco já usada.
/// </summary>
public sealed record GerarFolhaEtiquetasPdfCommand(
    FolhaSpec? Folha,
    bool Guias,
    int Pular,
    IReadOnlyList<EtiquetaNaFolha>? Etiquetas);

/// <param name="Modelo"><c>pimaco-a4256</c> ou <c>quantas-cabem</c>.</param>
/// <param name="Orientacao">Opcional. Quando vem, toda etiqueta precisa estar nessa orientação.</param>
/// <param name="MargemMm">Só para <c>quantas-cabem</c>. Padrão 5 mm.</param>
/// <param name="EspacoMm">Só para <c>quantas-cabem</c>. Padrão 3 mm entre etiquetas.</param>
public sealed record FolhaSpec(
    string? Modelo,
    string? Orientacao,
    [property: JsonPropertyName("margem_mm")] decimal? MargemMm,
    [property: JsonPropertyName("espaco_mm")] decimal? EspacoMm);

/// <param name="Layout">LayoutJson v1 resolvido, como objeto JSON (ou o mesmo JSON em texto).</param>
/// <param name="Copias">Padrão 1.</param>
public sealed record EtiquetaNaFolha(JsonElement Layout, int? Copias);
