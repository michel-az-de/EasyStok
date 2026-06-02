using System.Text.Json;

namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Ponte entre <see cref="Storefront"/> e <see cref="Produto"/> (ERP).
/// Cada CardapioItem aponta para um Produto e adiciona metadata pública
/// (foto, descrição, tag, filtros, ordem) consumida pela UI.
///
/// <para>
/// <strong>Preço efetivo</strong>: <see cref="PrecoStorefront"/> (override
/// específico do storefront) OU <c>Produto.PrecoReferencia</c> como fallback.
/// Permite vender o mesmo produto por preços diferentes em storefronts diferentes
/// sem duplicar Produto.
/// </para>
///
/// <para>
/// <strong>Default oculto</strong>: <see cref="Visivel"/> = false ao criar.
/// Babá aprova manualmente após importar do ERP — evita publicar produtos
/// sem revisar foto, descrição, alergênicos etc.
/// </para>
///
/// <para>
/// <strong>OrdemExibicao é double</strong> deliberadamente — permite inserir
/// entre 1.0 e 2.0 usando 1.5 sem renumerar a lista inteira.
/// </para>
/// </summary>
public class CardapioItem
{
    /// <summary>Tags públicas permitidas (filtros visuais na UI).</summary>
    private static readonly HashSet<string> TagsPermitidas =
        new(StringComparer.Ordinal) { "assinatura", "novo", "vegetariano" };

    public Guid Id { get; private set; }
    public Guid StorefrontId { get; private set; }
    public Guid ProdutoId { get; private set; }

    public bool Visivel { get; private set; }
    public bool Disponivel { get; private set; }

    /// <summary>
    /// Ordem de exibição em <see langword="double"/> intencionalmente — inserir
    /// entre 1.0 e 2.0 usando 1.5 evita renumerar todos os itens vizinhos.
    /// </summary>
    public double OrdemExibicao { get; private set; }

    public string? DescricaoPublica { get; private set; }
    public string? Ingredientes { get; private set; }
    public string? Alergenos { get; private set; }
    public string? SugestaoMolho { get; private set; }
    public string? TempoPreparo { get; private set; }
    public string? FotoUrl { get; private set; }

    /// <summary>Override do preço do <see cref="Produto"/>. Null = usa <c>Produto.PrecoReferencia</c>.</summary>
    public decimal? PrecoStorefront { get; private set; }

    /// <summary>Tag (assinatura, novo, vegetariano). Null = sem tag.</summary>
    public string? Tag { get; private set; }

    /// <summary>JSON array de strings, ex: <c>["sem-gluten","vegano"]</c>. Default <c>"[]"</c>.</summary>
    public string FiltrosJson { get; private set; } = "[]";

    /// <summary>Peso para exibição (ex: "500g"). Não usado em cálculo de frete.</summary>
    public string? PesoExibicao { get; private set; }

    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    /// <summary>Navegação para Produto. EF carrega via <c>Include</c>.</summary>
    public Produto? Produto { get; set; }

    // EF Core ctor sem parâmetros
    private CardapioItem() { }

    /// <summary>
    /// Factory: cria item a partir de Produto existente. Defaults seguros:
    /// <see cref="Visivel"/>=false, <see cref="Disponivel"/>=true, <see cref="FiltrosJson"/>="[]".
    /// </summary>
    public static CardapioItem CriarAPartirDeProduto(Guid storefrontId, Produto produto)
    {
        if (storefrontId == Guid.Empty)
            throw new RegraDeDominioVioladaException("StorefrontId é obrigatório.");

        if (produto is null)
            throw new RegraDeDominioVioladaException("Produto é obrigatório.");

        if (produto.Id == Guid.Empty)
            throw new RegraDeDominioVioladaException("Produto deve ter Id válido.");

        var agora = DateTime.UtcNow;
        return new CardapioItem
        {
            Id = Guid.NewGuid(),
            StorefrontId = storefrontId,
            ProdutoId = produto.Id,
            Visivel = false,         // safe default — Babá aprova manualmente
            Disponivel = true,
            OrdemExibicao = 0,
            FiltrosJson = "[]",
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    /// <summary>
    /// Preço efetivo: <see cref="PrecoStorefront"/> override OU
    /// <c>Produto.PrecoReferencia</c> OU zero (caso de borda, produto sem preço).
    /// </summary>
    public decimal PrecoEfetivo()
    {
        if (PrecoStorefront.HasValue)
            return PrecoStorefront.Value;

        if (Produto?.PrecoReferencia is not null)
            return Produto.PrecoReferencia.Valor;

        return 0m;
    }

    public void TornarVisivel()
    {
        if (Visivel) return;
        Visivel = true;
        AlteradoEm = DateTime.UtcNow;
    }

    public void Ocultar()
    {
        if (!Visivel) return;
        Visivel = false;
        AlteradoEm = DateTime.UtcNow;
    }

    public void MarcarEsgotado()
    {
        if (!Disponivel) return;
        Disponivel = false;
        AlteradoEm = DateTime.UtcNow;
    }

    public void MarcarDisponivel()
    {
        if (Disponivel) return;
        Disponivel = true;
        AlteradoEm = DateTime.UtcNow;
    }

    public void DefinirOrdem(double ordem)
    {
        if (ordem < 0)
            throw new RegraDeDominioVioladaException(
                $"Ordem de exibição não pode ser negativa (recebido: {ordem}).");

        if (Math.Abs(OrdemExibicao - ordem) < double.Epsilon) return; // idempotente
        OrdemExibicao = ordem;
        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Atualiza metadata opcional em bulk. Cada parâmetro <c>null</c> deixa o campo
    /// inalterado — exceto <paramref name="tag"/>, que pode ser explicitamente
    /// limpo via "" (string vazia) ou via reset semantics (não suportado aqui — use sentinel).
    /// </summary>
    public void AtualizarMetadata(
        string? descricaoPublica = null,
        string? ingredientes = null,
        string? alergenos = null,
        string? sugestaoMolho = null,
        string? tempoPreparo = null,
        string? fotoUrl = null,
        decimal? precoStorefront = null,
        string? tag = null,
        string? filtrosJson = null,
        string? pesoExibicao = null)
    {
        if (descricaoPublica is not null)
        {
            ValidarTamanho(descricaoPublica, max: 240, nome: "Descrição pública");
            DescricaoPublica = descricaoPublica.Trim();
        }

        if (ingredientes is not null)
        {
            ValidarTamanho(ingredientes, max: 500, nome: "Ingredientes");
            Ingredientes = ingredientes.Trim();
        }

        if (alergenos is not null)
        {
            ValidarTamanho(alergenos, max: 200, nome: "Alergênicos");
            Alergenos = alergenos.Trim();
        }

        if (sugestaoMolho is not null)
        {
            ValidarTamanho(sugestaoMolho, max: 200, nome: "Sugestão de molho");
            SugestaoMolho = sugestaoMolho.Trim();
        }

        if (tempoPreparo is not null)
        {
            ValidarTamanho(tempoPreparo, max: 50, nome: "Tempo de preparo");
            TempoPreparo = tempoPreparo.Trim();
        }

        if (fotoUrl is not null)
        {
            ValidarTamanho(fotoUrl, max: 500, nome: "URL da foto");
            FotoUrl = fotoUrl.Trim();
        }

        if (precoStorefront.HasValue)
        {
            if (precoStorefront.Value < 0m)
                throw new RegraDeDominioVioladaException(
                    $"Preço do storefront não pode ser negativo (recebido: {precoStorefront.Value:C}).");
            PrecoStorefront = precoStorefront.Value;
        }

        // null = "não tocar" (consistente com outros params). Use LimparTag() para remover.
        if (tag is not null)
        {
            var tagNormalizada = tag.Trim().ToLowerInvariant();
            if (!TagsPermitidas.Contains(tagNormalizada))
                throw new RegraDeDominioVioladaException(
                    $"Tag inválida: '{tag}'. Permitidas: {string.Join(", ", TagsPermitidas)}.");
            Tag = tagNormalizada;
        }

        if (filtrosJson is not null)
        {
            ValidarFiltrosJson(filtrosJson);
            FiltrosJson = filtrosJson;
        }

        if (pesoExibicao is not null)
        {
            ValidarTamanho(pesoExibicao, max: 50, nome: "Peso de exibição");
            PesoExibicao = pesoExibicao.Trim();
        }

        AlteradoEm = DateTime.UtcNow;
    }

    /// <summary>Remove a tag (sem efeito se já não tinha tag).</summary>
    public void LimparTag()
    {
        if (Tag is null) return;
        Tag = null;
        AlteradoEm = DateTime.UtcNow;
    }

    // ── Validações privadas ────────────────────────────────────────────

    private static void ValidarTamanho(string valor, int max, string nome)
    {
        if (valor.Length > max)
            throw new RegraDeDominioVioladaException(
                $"{nome} não pode exceder {max} caracteres (recebido: {valor.Length}).");
    }

    private static void ValidarFiltrosJson(string filtros)
    {
        try
        {
            using var doc = JsonDocument.Parse(filtros);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new RegraDeDominioVioladaException(
                    "FiltrosJson deve ser um array JSON (ex: [\"sem-gluten\"]).");

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String)
                    throw new RegraDeDominioVioladaException(
                        "FiltrosJson deve conter apenas strings.");
            }
        }
        catch (JsonException ex)
        {
            throw new RegraDeDominioVioladaException(
                $"FiltrosJson inválido: {ex.Message}", ex);
        }
    }
}
