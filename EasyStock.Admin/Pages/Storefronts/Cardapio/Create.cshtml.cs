using System.ComponentModel.DataAnnotations;

namespace EasyStock.Admin.Pages.Storefronts.Cardapio;

public class CreateModel(AdminApiClient api, AdminSessionService session, ILogger<CreateModel> log)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid StorefrontId { get; set; }
    [BindProperty] public CreateInput Input { get; set; } = new();

    public string StorefrontSlug { get; private set; } = "";
    public Guid EmpresaIdStorefront { get; private set; }

    /// <summary>Produtos da empresa do storefront que ainda não estão no cardápio.</summary>
    public IReadOnlyList<ProdutoOption> Produtos { get; private set; } = Array.Empty<ProdutoOption>();

    public sealed class CreateInput
    {
        [Required(ErrorMessage = "Escolha o produto.")] public Guid ProdutoId { get; set; }

        [Range(0, 1000)] public double OrdemExibicao { get; set; }

        public bool Visivel { get; set; }

        [StringLength(240)] public string? DescricaoPublica { get; set; }
        [StringLength(500)] public string? Ingredientes { get; set; }
        [StringLength(200)] public string? Alergenos { get; set; }
        [StringLength(200)] public string? SugestaoMolho { get; set; }
        [StringLength(50)] public string? TempoPreparo { get; set; }
        [StringLength(500)] public string? FotoUrl { get; set; }
        [Range(0, 100000)] public decimal? PrecoStorefront { get; set; }

        /// <summary>Valores permitidos: "assinatura", "novo", "vegetariano", ou vazio.</summary>
        public string? Tag { get; set; }

        [StringLength(50)] public string? PesoExibicao { get; set; }

        [StringLength(500)] public string? Motivo { get; set; }
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
        if (!ModelState.IsValid)
        {
            await CarregarContextoAsync();
            return Page();
        }

        try
        {
            var resp = await api.PostAsync<JsonElement>(
                $"api/admin/storefronts/{StorefrontId}/cardapio",
                new
                {
                    produtoId = Input.ProdutoId,
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
                    pesoExibicao = Input.PesoExibicao,
                    motivo = Input.Motivo
                });

            SetSucesso("Item adicionado ao cardápio.");
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
                var rawCard = await api.GetRawAsync($"api/admin/storefronts/{StorefrontId}/cardapio");
                var jaUsados = new HashSet<Guid>();
                if (rawCard.TryGetProperty("data", out var dCard) && dCard.TryGetProperty("itens", out var itArr))
                {
                    foreach (var it in itArr.EnumerateArray())
                    {
                        if (it.TryGetProperty("produtoId", out var pid)
                            && Guid.TryParse(pid.GetString(), out var pidGuid))
                        {
                            jaUsados.Add(pidGuid);
                        }
                    }
                }
                Produtos = produtosList.Where(p => !jaUsados.Contains(p.Id)).ToList();
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Falha ao carregar contexto p/ adicionar cardápio item ({StorefrontId})", StorefrontId);
            Produtos = Array.Empty<ProdutoOption>();
        }
    }
}
