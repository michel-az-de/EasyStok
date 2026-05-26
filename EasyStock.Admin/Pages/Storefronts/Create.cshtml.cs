using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyStock.Admin.Pages.Storefronts;

public class CreateModel(AdminApiClient api, AdminSessionService session, ILogger<CreateModel> log)
    : AdminPageBase(session)
{
    /// <summary>Slug normalizado: 3-40 chars lowercase, sem hífens duplos/laterais.</summary>
    public static readonly Regex SlugRegex = new(
        @"^[a-z0-9]([a-z0-9]|-(?!-)){1,38}[a-z0-9]$",
        RegexOptions.Compiled);

    [BindProperty] public CreateInput Input { get; set; } = new();

    /// <summary>Lista de empresas para popular o picker. Carregada em OnGet.</summary>
    public IReadOnlyList<EmpresaOption> Empresas { get; private set; } = Array.Empty<EmpresaOption>();

    public sealed class CreateInput
    {
        [Required(ErrorMessage = "Escolha a empresa.")]
        public Guid EmpresaId { get; set; }

        [Required(ErrorMessage = "Slug é obrigatório.")]
        [StringLength(40, MinimumLength = 3, ErrorMessage = "Slug deve ter entre 3 e 40 caracteres.")]
        [RegularExpression(
            @"^[a-z0-9]([a-z0-9]|-(?!-)){1,38}[a-z0-9]$",
            ErrorMessage = "Slug aceita apenas letras minúsculas, números e hífen (não pode iniciar/terminar com hífen, sem hífens consecutivos).")]
        public string Slug { get; set; } = string.Empty;

        [Required(ErrorMessage = "Título público é obrigatório.")]
        [StringLength(120, ErrorMessage = "Título não pode passar de 120 caracteres.")]
        public string TituloPublico { get; set; } = string.Empty;

        [Range(0, 100000, ErrorMessage = "Pedido mínimo deve ser entre 0 e 100.000.")]
        public decimal PedidoMinimoEntrega { get; set; }

        [StringLength(500, ErrorMessage = "Motivo não pode passar de 500 caracteres.")]
        public string? Motivo { get; set; }
    }

    public sealed record EmpresaOption(Guid Id, string Nome, string? Documento, bool JaTemStorefront);

    public async Task<IActionResult> OnGetAsync()
    {
        await CarregarEmpresasAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await CarregarEmpresasAsync();
            return Page();
        }

        try
        {
            var resp = await api.PostAsync<JsonElement>("api/admin/storefronts", new
            {
                empresaId = Input.EmpresaId,
                slug = Input.Slug.Trim().ToLowerInvariant(),
                tituloPublico = Input.TituloPublico.Trim(),
                pedidoMinimoEntrega = Input.PedidoMinimoEntrega,
                motivo = Input.Motivo
            });

            var id = resp.TryGetProperty("storefrontId", out var idP) ? idP.GetGuid() : Guid.Empty;
            SetSucesso($"Storefront '{Input.Slug.ToLowerInvariant()}' criado com sucesso.");
            return RedirectToPage("/Storefronts/Detail", new { id });
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao criar storefront");
            SetErroSeguro(ex, "Criação");
            await CarregarEmpresasAsync();
            return Page();
        }
    }

    private async Task CarregarEmpresasAsync()
    {
        try
        {
            // pageSize alto = MVP. Em scale > 200 tenants, isso vira typeahead async.
            // Hoje (Casa da Babá + alguns demos) é OK.
            var raw = await api.GetRawAsync("api/admin/tenants?page=1&pageSize=200");
            var data = raw.TryGetProperty("data", out var d) ? d : default;
            if (data.ValueKind != JsonValueKind.Array)
            {
                Empresas = Array.Empty<EmpresaOption>();
                return;
            }

            var list = new List<EmpresaOption>();
            foreach (var t in data.EnumerateArray())
            {
                if (!t.TryGetProperty("id", out var idP) || !Guid.TryParse(idP.GetString(), out var id))
                    continue;
                var nome = t.TryGetProperty("nome", out var nP) ? nP.GetString() ?? "(sem nome)" : "(sem nome)";
                var doc = t.TryGetProperty("documento", out var dP2) ? dP2.GetString() : null;
                // Mostra "(já tem)" se possível. AdminTenantsController não expõe esse
                // campo hoje — deixamos sempre false e o backend valida no POST.
                list.Add(new EmpresaOption(id, nome, doc, JaTemStorefront: false));
            }
            Empresas = list;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Falha ao carregar empresas para picker");
            Empresas = Array.Empty<EmpresaOption>();
        }
    }
}
