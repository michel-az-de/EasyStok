using EasyStock.Web.Infrastructure;
using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Cardapio;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Gestão self-service do cardápio da vitrine (Fase 4 do ADR-0031). Fala com a Api via
/// <see cref="CardapioService"/> (/api/minha-vitrine/cardapio). Restrito a Admin/Owner —
/// espelha o Policy=Admin da Api; usuário sem papel é barrado antes de ver um 403.
/// Mutações são AJAX (JSON) para a galeria não recarregar a página.
/// </summary>
public class CardapioController(
    CardapioService svc,
    ProdutosService produtosSvc,
    SessionService session,
    IConfiguration config) : BaseController(session)
{
    private const string ActiveKey = "Cardapio";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        base.OnActionExecuting(context);
        if (context.Result is not null) return; // base já resolveu (sessão/loja)

        // Gate de papel: só Admin/SuperAdmin gerencia a vitrine (igual à Api).
        if (!IsAdmin())
        {
            if (AjaxRequest.WantsJson(context.HttpContext.Request))
            {
                context.Result = new JsonResult(new { ok = false, erro = "Acesso restrito ao administrador." })
                { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
            TempData["Toast"] = "warning|Acesso restrito: só administradores gerenciam o cardápio.";
            context.Result = RedirectToAction("Index", "Dashboard");
        }
    }

    [HttpGet("/cardapio")]
    public async Task<IActionResult> Index()
    {
        ViewBag.ActiveMenuItem = ActiveKey;

        var vitrine = await svc.ObterVitrineAsync();
        var vm = new CardapioIndexViewModel();

        if (!vitrine.Success || vitrine.Data is null)
        {
            // 404 = empresa sem vitrine (estado dedicado). Outro erro: avisa, mas mostra o mesmo estado.
            vm.TemVitrine = false;
            if (vitrine.HttpStatus is not (404 or 0) && !vitrine.Success)
                ToastError("Não foi possível carregar a vitrine agora.");
            return View(vm);
        }

        vm.TemVitrine = true;
        vm.Slug = vitrine.Data.Slug;
        vm.TituloVitrine = vitrine.Data.TituloPublico;
        vm.VitrineAtiva = vitrine.Data.Ativo;

        var publicApi = (config["PublicApiUrl"] ?? string.Empty).TrimEnd('/');
        if (publicApi.Length > 0)
            vm.ImprimirUrl = $"{publicApi}/api/storefront/{vitrine.Data.Slug}/menu/imprimir";

        var lista = await svc.ListarAsync();
        if (lista.Success && lista.Data is not null)
        {
            vm.Total = lista.Data.Itens.Count;
            vm.Grupos = lista.Data.Itens
                .GroupBy(i => string.IsNullOrWhiteSpace(i.CategoriaTexto) ? "Sem categoria" : i.CategoriaTexto!.Trim())
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new CardapioCategoriaGrupo
                {
                    Categoria = g.Key,
                    Itens = g.OrderBy(x => x.OrdemExibicao).ToList()
                })
                .ToList();
        }

        return View(vm);
    }

    [HttpGet("/cardapio/novo")]
    public async Task<IActionResult> Novo()
    {
        ViewBag.ActiveMenuItem = ActiveKey;

        var vitrine = await svc.ObterVitrineAsync();
        if (!vitrine.Success || vitrine.Data is null)
        {
            TempData["Toast"] = "warning|Sua vitrine ainda não está pronta. Fale com o suporte.";
            return RedirectToAction(nameof(Index));
        }

        await PreencherDadosFormAsync();
        return View("Form", new CardapioItemFormViewModel { Modo = "avulso", Publicar = false });
    }

    [HttpGet("/cardapio/{id:guid}/editar")]
    public async Task<IActionResult> Editar(Guid id)
    {
        ViewBag.ActiveMenuItem = ActiveKey;

        var r = await svc.ObterItemAsync(id);
        if (!r.Success || r.Data is null)
        {
            TempData["Toast"] = "error|Item não encontrado.";
            return RedirectToAction(nameof(Index));
        }

        var d = r.Data;
        var vm = new CardapioItemFormViewModel
        {
            Id = d.Id,
            Modo = d.Avulso ? "avulso" : "vinculado",
            ProdutoId = d.ProdutoId,
            ProdutoNome = d.Avulso ? null : d.NomeEfetivo,
            NomePublico = d.NomePublico,
            PrecoStorefront = d.PrecoStorefront,
            CategoriaTexto = d.CategoriaTexto,
            DescricaoPublica = d.DescricaoPublica,
            Ingredientes = d.Ingredientes,
            Alergenos = d.Alergenos,
            SugestaoMolho = d.SugestaoMolho,
            TempoPreparo = d.TempoPreparo,
            PesoExibicao = d.PesoExibicao,
            FotoUrl = d.FotoUrl,
            Disponivel = d.Disponivel,
            Publicar = d.Visivel,
        };

        ViewBag.NomeEfetivo = d.NomeEfetivo;
        ViewBag.PrecoEfetivo = d.PrecoEfetivo;
        await PreencherDadosFormAsync();
        return View("Form", vm);
    }

    [HttpPost("/cardapio/criar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar([FromForm] CardapioItemFormViewModel vm, IFormFile? foto)
    {
        var r = await svc.CriarAsync(MapToFormApi(vm));
        if (!r.Success || r.Data is null)
            return JsonFail(MensagemErro(r.ErrorMessage, r.ErrorCode));

        var itemId = r.Data.ItemId;

        // create→upload não-atômico: se a foto falhar, o item fica como rascunho e avisamos —
        // o arquivo não some em silêncio (o usuário reenvia na edição).
        string? avisoFoto = null;
        if (foto is { Length: > 0 })
        {
            var up = await svc.UploadFotoAsync(itemId, foto);
            if (!up.Success)
                avisoFoto = "Item criado, mas a foto não subiu. Edite o item para tentar de novo.";
        }

        return JsonOk(new { id = itemId, avisoFoto });
    }

    [HttpPost("/cardapio/{id:guid}/atualizar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Atualizar(Guid id, [FromForm] CardapioItemFormViewModel vm)
    {
        var r = await svc.EditarAsync(id, MapToFormApi(vm));
        if (!r.Success)
            return JsonFail(MensagemErro(r.ErrorMessage, r.ErrorCode));
        return JsonOk(new { id });
    }

    [HttpPost("/cardapio/{id:guid}/foto")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadFoto(Guid id, IFormFile foto)
    {
        if (foto is null || foto.Length == 0)
            return JsonFail("Selecione uma imagem.");
        if (foto.Length > 6 * 1024 * 1024)
            return JsonFail("A imagem não pode ser maior que 6 MB.");

        var r = await svc.UploadFotoAsync(id, foto);
        if (!r.Success || r.Data is null)
            return JsonFail(MensagemErro(r.ErrorMessage, r.ErrorCode));
        return JsonOk(new { url = r.Data.Url });
    }

    [HttpPost("/cardapio/{id:guid}/publicar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publicar(Guid id)
    {
        var r = await svc.TogglePublicarAsync(id);
        if (!r.Success || r.Data is null)
            return JsonFail(MensagemErro(r.ErrorMessage, r.ErrorCode));
        return JsonOk(new { visivel = r.Data.VisivelAgora });
    }

    [HttpPost("/cardapio/{id:guid}/disponivel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disponivel(Guid id)
    {
        var r = await svc.ToggleDisponivelAsync(id);
        if (!r.Success || r.Data is null)
            return JsonFail(MensagemErro(r.ErrorMessage, r.ErrorCode));
        return JsonOk(new { disponivel = r.Data.DisponivelAgora });
    }

    public sealed record ReordenarRequest(double NovaOrdem);

    [HttpPost("/cardapio/{id:guid}/reordenar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reordenar(Guid id, [FromBody] ReordenarRequest req)
    {
        var r = await svc.ReordenarAsync(id, req.NovaOrdem);
        if (!r.Success)
            return JsonFail(MensagemErro(r.ErrorMessage, r.ErrorCode));
        return JsonOk();
    }

    [HttpPost("/cardapio/{id:guid}/remover")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remover(Guid id)
    {
        var r = await svc.RemoverAsync(id);
        if (!r.Success)
            return JsonFail(MensagemErro(r.ErrorMessage, r.ErrorCode));
        return JsonOk(new { id });
    }

    [HttpGet("/cardapio/buscar-produtos")]
    public async Task<IActionResult> BuscarProdutos(string termo)
    {
        if (string.IsNullOrWhiteSpace(termo) || termo.Trim().Length < 2)
            return Json(Array.Empty<object>());

        var r = await produtosSvc.BuscarAsync(termo.Trim(), 10);
        if (!r.Success || r.Data is null)
            return Json(Array.Empty<object>());

        return Json(r.Data.Select(p => new { id = p.Id, nome = p.Nome }));
    }

    // ── helpers ────────────────────────────────────────────────────────────

    // Contrato null/"" do backend: NomePublico vazio vira null (vinculado herda; avulso é validado
    // no form). Demais opcionais vão como vêm — "" limpa o campo. Tag/Filtros: null = preserva (sem
    // controle no form v1). Visivel só no criar; no editar a Api ignora (toggle dedicado).
    private static CardapioItemFormApi MapToFormApi(CardapioItemFormViewModel vm)
    {
        var vinculado = string.Equals(vm.Modo, "vinculado", StringComparison.OrdinalIgnoreCase) && vm.ProdutoId.HasValue;
        var nome = string.IsNullOrWhiteSpace(vm.NomePublico) ? null : vm.NomePublico!.Trim();

        return new CardapioItemFormApi(
            ProdutoId: vinculado ? vm.ProdutoId : null,
            NomePublico: nome,
            PrecoStorefront: vm.PrecoStorefront,
            CategoriaTexto: vm.CategoriaTexto,
            DescricaoPublica: vm.DescricaoPublica,
            Ingredientes: vm.Ingredientes,
            Alergenos: vm.Alergenos,
            SugestaoMolho: vm.SugestaoMolho,
            TempoPreparo: vm.TempoPreparo,
            PesoExibicao: vm.PesoExibicao,
            Tag: null,
            Visivel: vm.Publicar);
    }

    // Categorias existentes (sugestões do datalist) — distintas dos itens atuais da vitrine.
    private async Task PreencherDadosFormAsync()
    {
        var lista = await svc.ListarAsync();
        var categorias = lista.Success && lista.Data is not null
            ? lista.Data.Itens
                .Where(i => !string.IsNullOrWhiteSpace(i.CategoriaTexto))
                .Select(i => i.CategoriaTexto!.Trim())
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
            : new List<string>();

        // ProdutoIds já vinculados — o typeahead do modo vinculado os exclui (evita item duplicado).
        var vinculados = lista.Success && lista.Data is not null
            ? lista.Data.Itens.Where(i => i.ProdutoId.HasValue).Select(i => i.ProdutoId!.Value.ToString()).ToList()
            : new List<string>();

        ViewBag.Categorias = categorias;
        ViewBag.ProdutosVinculados = vinculados;
    }

    private static string MensagemErro(string? mensagem, string? codigo) =>
        !string.IsNullOrWhiteSpace(mensagem)
            ? mensagem!
            : codigo switch
            {
                "NOT_FOUND" => "Item não encontrado.",
                "VALIDATION_ERROR" => "Revise os campos e tente de novo.",
                _ => "Não foi possível salvar agora. Tente de novo em instantes."
            };
}
