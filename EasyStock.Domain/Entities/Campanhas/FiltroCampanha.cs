using System.Text.Json;

namespace EasyStock.Domain.Entities.Campanhas;

/// <summary>
/// Quem a campanha quer alcançar (S28). Guardado como JSON em <see cref="Campanha.FiltroJson"/>; o
/// cálculo do público é da S29. Tags normalizadas como <see cref="ClienteTag"/>. "Todos" precisa ser
/// escolhido explicitamente: filtro vazio não alcança ninguém e não agenda.
/// </summary>
public sealed record FiltroCampanha(
    bool Todos,
    IReadOnlyList<string> TagsIncluir,
    IReadOnlyList<string> TagsExcluir,
    Guid? ComprouItemId,
    int? ComprouNosUltimosDias)
{
    public const int DiasMaximo = 365;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static FiltroCampanha ParaTodos { get; } = new(true, [], [], null, null);

    public static FiltroCampanha Vazio { get; } = new(false, [], [], null, null);

    /// <summary>Tem algum critério de inclusão (todos, tag ou compra).</summary>
    public bool TemPublico => Todos || TagsIncluir.Count > 0 || ComprouItemId is not null;

    /// <summary>Normaliza as tags e valida o critério de compra.</summary>
    public FiltroCampanha Normalizado()
    {
        if (ComprouItemId is null && ComprouNosUltimosDias is not null)
            throw new RegraDeDominioVioladaException("Informe o item comprado junto com os dias.");
        if (ComprouItemId is not null && ComprouNosUltimosDias is not (> 0 and <= DiasMaximo))
            throw new RegraDeDominioVioladaException($"Dias da compra entre 1 e {DiasMaximo}.");

        return this with
        {
            TagsIncluir = Campanha.NormalizarTags(TagsIncluir),
            TagsExcluir = Campanha.NormalizarTags(TagsExcluir),
        };
    }

    public string ParaJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static FiltroCampanha DeJson(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Vazio
            : JsonSerializer.Deserialize<FiltroCampanha>(json, JsonOptions) ?? Vazio;
}
