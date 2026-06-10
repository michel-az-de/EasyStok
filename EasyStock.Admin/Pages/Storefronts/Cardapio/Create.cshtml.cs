using System.ComponentModel.DataAnnotations;

namespace EasyStock.Admin.Pages.Storefronts.Cardapio;

public class CreateModel(AdminApiClient api, AdminSessionService session, ILogger<CreateModel> log)
    : AdminPageBase(session)
{
    protected override bool PermiteNivelAdmin => true;

    [BindProperty(SupportsGet = true)] public Guid StorefrontId { get; set; }
    [BindProperty] public CreateInput Input { get; set; } = new();

    public string StorefrontSlug { get; private set; } = "";
    public Guid EmpresaIdStorefront { get; private set; }

    /// <summary>Produtos da empresa do storefront que ainda não estão no cardápio (modo vinculado).</summary>
    public IReadOnlyList<ProdutoOption> Produtos { get; private set; } = Array.Empty<ProdutoOption>();

    /// <summary>Categorias já usadas no cardápio (sugestões para o datalist de item avulso).</summary>
    public IReadOnlyList<string> CategoriasExistentes { get; private set; } = Array.Empty<string>();

    public sealed class CreateInput
    {
        /// <summary>"avulso" (item novo, sem ERP) ou "vinculado" (usa Produto do estoque). Default: avulso.</summary>
        public string Modo { get; set; } = "avulso";

        /// <summary>Preenchido só no modo vinculado.</summary>
        public Guid? ProdutoId { get; set; }

        /// <summary>Nome do item — obrigatório no modo avulso.</summary>
        [StringLength(200)] public string? NomePublico { get; set; }

        /// <summary>Categoria de exibição (livre). Usada para agrupar no cardápio público.</summary>
        [StringLength(100)] public string? CategoriaTexto { get; set; }

        [Range(0, 1000)] public double OrdemExibicao { get; set; }

        public bool Visivel { get; set; }

        [StringLength(240)] public string? DescricaoPublica { get; set; }
        [StringLength(500)] public string? Ingredientes { get; set; }
        [StringLength(200)] public string? Alergenos { get; set; }
        [StringLength(200)] public string? SugestaoMolho { get; set; }
        [StringLength(50)] public string? TempoPreparo { get; set; }
        [StringLength(500)] public string? FotoUrl { get; set; }

        /// <summary>Preço em R$. Obrigatório no modo avulso; override opcional no vinculado.</summary>
        [Range(0, 100000)] public decimal? PrecoStorefront { get; set; }

        /// <summary>Valores permitidos: "assinatura", "novo", "vegetariano", ou vazio.</summary>
        public string? Tag { get; set; }

        [StringLength(50)] public string? PesoExibicao { get; set; }
    }

    public sealed record ProdutoOption(Guid Id, string Nome, decimal? PrecoReferencia);

    public async Task<IActionResult> OnGetAsync()
    {
        if (StorefrontId == Guid.Empty) return RedirectToPage("/Storefronts/Index");
        await CarregarContextoAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var avulso = !string.Equals(Input.Modo, "vinculado", StringComparison.OrdinalIgnoreCase);

        // Validação condicional por modo (não dá pra usar [Required] estático).
        if (avulso)
        {
            if (string.IsNullOrWhiteSpace(Input.NomePublico))
                ModelState.AddModelError("Input.NomePublico", "Informe o nome do item.");
            if (!Input.PrecoStorefront.HasValue || Input.PrecoStorefront.Value <= 0m)
                ModelState.AddModelError("Input.PrecoStorefront", "Informe um preço maior que zero.");
        }
        else if (!Input.ProdutoId.HasValue || Input.ProdutoId.Value == Guid.Empty)
        {
            ModelState.AddModelError("Input.ProdutoId", "Escolha o produto do estoque.");
        }

        if (!ModelState.IsValid)
        {
            await CarregarContextoAsync();
            return Page();
        }

        try
        {
            await api.PostAsync<JsonElement>(
                $"api/admin/storefronts/{StorefrontId}/cardapio",
                new
                {
                    // Avulso envia produtoId null + nomePublico; vinculado envia produtoId.
                    produtoId = avulso ? (Guid?)null : Input.ProdutoId,
                    nomePublico = avulso ? Input.NomePublico : null,
                    categoriaTexto = string.IsNullOrWhiteSpace(Input.CategoriaTexto) ? null : Input.CategoriaTexto,
                    ordemExibicao = Input.OrdemExibicao,
                    visivel = Input.Visivel,
                    descricaoPublica = Input.DescricaoPublica,
                    ingredientes = Input.Ingredientes,
                    alergenos = Input.Alergenos,
                    sugestaoMolho = Input.SugestaoMolho,
                    tempoPreparo = Input.TempoPreparo,
                    fotoUrl = Input.FotoUrl,
                    precoStorefront = Input.PrecoStorefront,
                    tag = string.IsNullOrWhiteSpace(Input.Tag) ? null : Input.Tag,
                    pesoExibicao = Input.PesoExibicao
                });

            SetSucesso(Input.Visivel ? "Item adicionado e publicado." : "Item salvo como rascunho.");
            return RedirectToPage("/Storefronts/Cardapio/Index", new { storefrontId = StorefrontId });
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao adicionar item ao cardápio {StorefrontId}", StorefrontId);
            SetErroSeguro(ex, "Adição");
            await CarregarContextoAsync();
            return Page();
        }
    }

    private async Task CarregarContextoAsync()
    {
        try
        {
            // 1. Storefront info (precisa de empresaId para listar produtos)
            var rawSf = await api.GetRawAsync($"api/admin/storefronts/{StorefrontId}");
            if (rawSf.TryGetProperty("data", out var dSf))
            {
                StorefrontSlug = dSf.TryGetProperty("slug", out var s) ? s.GetString() ?? "" : "";
                if (dSf.TryGetProperty("empresaId", out var e) && Guid.TryParse(e.GetString(), out var empId))
                {
                    EmpresaIdStorefront = empId;
                }
            }

            // 2. Produtos da empresa
            if (EmpresaIdStorefront != Guid.Empty)
            {
                var rawProd = await api.GetRawAsync(
                    $"api/empresas/{EmpresaIdStorefront}/produtos?page=1&pageSize=200&status=Ativo");
                var produtosList = new List<ProdutoOption>();
                if (rawProd.TryGetProperty("data", out var dProd) && dProd.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in dProd.EnumerateArray())
                    {
                        if (!p.TryGetProperty("id", out var idP) || !Guid.TryParse(idP.GetString(), out var id))
                            continue;
                        var nome = p.TryGetProperty("nome", out var nP) ? nP.GetString() ?? "(sem nome)" : "(sem nome)";
                        decimal? prc = null;
                        if (p.TryGetProperty("precoReferencia", out var pr) && pr.ValueKind == JsonValueKind.Number)
                            prc = pr.GetDecimal();
                        produtosList.Add(new ProdutoOption(id, nome, prc));
                    }
                }

                // 3. Filtrar produtos já no cardápio (call separado p/ não bloquear o pageload)
                //    + coletar categorias existentes para o datalist do item avulso.
                var rawCard = await api.GetRawAsync($"api/admin/storefronts/{StorefrontId}/cardapio");
                var jaUsados = new HashSet<Guid>();
                var categorias = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (rawCard.TryGetProperty("data", out var dCard) && dCard.TryGetProperty("itens", out var itArr))
                {
                    foreach (var it in itArr.EnumerateArray())
                    {
                        if (it.TryGetProperty("produtoId", out var pid)
                            && pid.ValueKind != JsonValueKind.Null
                            && Guid.TryParse(pid.GetString(), out var pidGuid))
                        {
                            jaUsados.Add(pidGuid);
                        }
                        if (it.TryGetProperty("categoriaTexto", out var cat)
                            && cat.ValueKind == JsonValueKind.String
                            && !string.IsNullOrWhiteSpace(cat.GetString()))
                        {
                            categorias.Add(cat.GetString()!);
                        }
                    }
                }
                Produtos = produtosList.Where(p => !jaUsados.Contains(p.Id)).ToList();
                CategoriasExistentes = categorias.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Falha ao carregar contexto p/ adicionar cardápio item ({StorefrontId})", StorefrontId);
            Produtos = Array.Empty<ProdutoOption>();
        }
    }
}
