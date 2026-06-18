using System.Text.Json;

namespace EasyStock.Domain.Entities.Storefront;

/// <summary>
/// Item de cardápio público de um <see cref="Storefront"/>.
/// Suporta dois modos que coexistem no mesmo storefront:
///
/// <list type="bullet">
/// <item><term>Avulso</term>
/// <description>Sem <see cref="ProdutoId"/>. Nome e preço são próprios do item
/// (<see cref="NomePublico"/> obrigatório, <see cref="PrecoStorefront"/> obrigatório).
/// Útil para tenants sem ERP (ex: Casa da Baba adicionando "Lasanha Bolonhesa").</description></item>
/// <item><term>Vinculado</term>
/// <description>Com <see cref="ProdutoId"/> (FK para Produto do ERP).
/// <see cref="NomePublico"/> e <see cref="PrecoStorefront"/> são overrides opcionais;
/// quando null, herda de <c>Produto.Nome</c> e <c>Produto.PrecoReferencia</c>.</description></item>
/// </list>
///
/// <para>
/// <strong>Invariante de banco</strong>: CHECK constraint garante
/// <c>produto_id IS NOT NULL OR nome_publico IS NOT NULL</c>.
/// </para>
///
/// <para>
/// <strong>Default oculto</strong>: <see cref="Visivel"/> = false ao criar.
/// Tenant publica manualmente — evita itens sem foto/descrição revisados irem ao ar.
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

    /// <summary>
    /// FK para Produto do ERP. Null = item avulso (sem vínculo com ERP).
    /// Invariante: <see cref="NomePublico"/> obrigatório quando null.
    /// </summary>
    public Guid? ProdutoId { get; private set; }

    /// <summary>
    /// Nome exibido no cardápio público. Armazenado em lowercase.
    /// Obrigatório para itens avulsos (<see cref="ProdutoId"/> = null).
    /// Para vinculados: override de <c>Produto.Nome</c> quando presente.
    /// </summary>
    public string? NomePublico { get; private set; }

    /// <summary>
    /// Categoria de exibição no cardápio. Armazenada em lowercase.
    /// Para avulsos: categoria livre do tenant.
    /// Para vinculados: override de <c>Produto.Categoria.Nome</c> quando presente.
    /// </summary>
    public string? CategoriaTexto { get; private set; }

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
    /// Factory: cria item <strong>avulso</strong> sem vínculo com Produto do ERP.
    /// Útil para tenants que não usam inventário (ex: Casa da Baba).
    /// </summary>
    /// <param name="storefrontId">Storefront onde o item será exibido.</param>
    /// <param name="nome">Nome exibido no cardápio. Armazenado em lowercase.</param>
    /// <param name="precoEmReais">Preço em R$ (decimal, ex: 35.00m). Deve ser positivo.</param>
    /// <param name="categoria">Categoria opcional. Armazenada em lowercase.</param>
    public static CardapioItem CriarAvulso(
        Guid storefrontId,
        string nome,
        decimal precoEmReais,
        string? categoria = null)
    {
        if (storefrontId == Guid.Empty)
            throw new RegraDeDominioVioladaException("StorefrontId é obrigatório.");

        if (string.IsNullOrWhiteSpace(nome))
            throw new RegraDeDominioVioladaException("Nome é obrigatório para item avulso.");

        ValidarTamanho(nome, max: 200, nome: "Nome do item");

        if (precoEmReais <= 0m)
            throw new RegraDeDominioVioladaException(
                // BUG-011: :C com cultura invariante imprime '¤'/formato contábil EN — usa R$ + número simples.
                $"Preço deve ser positivo (recebido: R$ {precoEmReais:0.00}).");

        // BUG-004: teto p/ não estourar a coluna decimal(10,2) (erro técnico vazado ao cliente).
        if (precoEmReais > 999_999.99m)
            throw new RegraDeDominioVioladaException(
                "Preço acima do limite permitido (máximo R$ 999.999,99).");

        // BUG-010: moeda com no máximo 2 casas (a coluna decimal(10,2) arredonda; tornamos explícito).
        precoEmReais = Math.Round(precoEmReais, 2, MidpointRounding.AwayFromZero);

        if (categoria is not null)
            ValidarTamanho(categoria, max: 100, nome: "Categoria");

        var agora = DateTime.UtcNow;
        return new CardapioItem
        {
            Id = Guid.NewGuid(),
            StorefrontId = storefrontId,
            ProdutoId = null,                                            // avulso
            NomePublico = nome.Trim().ToLowerInvariant(),
            CategoriaTexto = categoria?.Trim().ToLowerInvariant(),
            PrecoStorefront = precoEmReais,
            Visivel = false,         // safe default — tenant publica manualmente
            Disponivel = true,
            OrdemExibicao = 0,
            FiltrosJson = "[]",
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    /// <summary>
    /// Factory: cria item <strong>vinculado</strong> a Produto existente do ERP.
    /// Defaults seguros: <see cref="Visivel"/>=false, <see cref="Disponivel"/>=true.
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
            Visivel = false,         // safe default — tenant publica manualmente
            Disponivel = true,
            OrdemExibicao = 0,
            FiltrosJson = "[]",
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    /// <summary>
    /// Nome efetivo: <see cref="NomePublico"/> override OU <c>Produto.Nome</c>.
    /// Retorna null apenas se avulso sem NomePublico (estado inválido — CHECK no banco evita).
    /// </summary>
    public string? NomeEfetivo() => NomePublico ?? Produto?.Nome;

    /// <summary>
    /// Categoria efetiva: <see cref="CategoriaTexto"/> override OU <c>Produto.Categoria.Nome</c>.
    /// </summary>
    public string? CategoriaEfetiva() => CategoriaTexto ?? Produto?.Categoria?.Nome;

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
        string? nomePublico = null,
        string? categoriaTexto = null,
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
        if (nomePublico is not null)
        {
            if (string.IsNullOrWhiteSpace(nomePublico))
                throw new RegraDeDominioVioladaException("Nome não pode ser vazio.");
            ValidarTamanho(nomePublico, max: 200, nome: "Nome do item");
            NomePublico = nomePublico.Trim().ToLowerInvariant();
        }

        if (categoriaTexto is not null)
        {
            ValidarTamanho(categoriaTexto, max: 100, nome: "Categoria");
            CategoriaTexto = string.IsNullOrWhiteSpace(categoriaTexto)
                ? null
                : categoriaTexto.Trim().ToLowerInvariant();
        }

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
                    // BUG-011: evita '¤'/formato contábil EN do :C com cultura invariante.
                    $"Preço do storefront não pode ser negativo (recebido: R$ {precoStorefront.Value:0.00}).");
            if (precoStorefront.Value > 999_999.99m)
                throw new RegraDeDominioVioladaException(
                    "Preço acima do limite permitido (máximo R$ 999.999,99).");
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
