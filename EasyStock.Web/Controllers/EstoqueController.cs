using System.Text;
using EasyStock.Web.Helpers;
using EasyStock.Web.Models.ViewModels.Entradas;
using EasyStock.Web.Models.ViewModels.Estoque;
using EasyStock.Web.Models.ViewModels.Saidas;
using EasyStock.Web.Models.ViewModels.Shared;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class EstoqueController(
    EstoqueService svc,
    SaidasService saidasSvc,
    EntradasService entradasSvc,
    SessionService session,
    ILogger<EstoqueController> log) : BaseController(session)
{
    [HttpGet("/estoque")]
    public async Task<IActionResult> Index(int page = 1, string? search = null, string? status = null, string? categoria = null)
    {
        ViewBag.Title = "Estoque";
        ViewBag.ActiveMenuItem = "Estoque";

        // Listagem paginada + contadores agregados em paralelo. Contadores
        // separam "cadastrados" (= Paginacao.Total, inclui qty 0) de "com saldo"
        // (qty > 0) pra que o cabecalho da tela bata com o KPI "Unidades em
        // estoque" do dashboard, que conta unidades dos lotes com saldo.
        // Skip nos contadores quando ha search ativo: a busca usa endpoint
        // separado (estoque/buscar) que nao tem paginacao real, entao contar
        // o universo total nao agrega valor.
        var listarTask = svc.ListarAsync(page, status, categoria, search);
        var contadoresTask = string.IsNullOrEmpty(search)
            ? svc.ContadoresAsync(status, categoria)
            : null;

        var result = await listarTask;
        var contadoresResp = contadoresTask is null ? null : await contadoresTask;

        // Em erro (incluindo 401 silencioso), preserva filtros do querystring no VM
        // pra que a pill ativa, search e categoria não sumam ao primeiro problema de
        // API — caso contrário deep-links tipo /estoque?status=critico parecem quebrados
        // (caem em VM vazio com StatusFiltro=null, ativa "Todos" e o operador acha que
        // o filtro não aplicou). É consequência prática do bug #2A.
        if (HasError(result))
            return View(new EstoqueListViewModel
            {
                Search = search,
                StatusFiltro = status,
                Categoria = categoria
            });

        var paged = result.Data!;
        var contadores = contadoresResp is { Success: true, Data: { } d } ? d : null;
        var vm = new EstoqueListViewModel
        {
            Itens = paged.Data,
            Search = search,
            StatusFiltro = status,
            Categoria = categoria,
            Paginacao = new PaginationViewModel
            {
                Page = paged.Meta.Page,
                Pages = paged.Meta.Pages,
                Total = paged.Meta.Total,
                Limit = paged.Meta.Limit
            },
            LotesCadastrados = contadores?.Cadastrados,
            LotesComSaldo = contadores?.ComSaldo
        };
        return View(vm);
    }

    [HttpGet("/estoque/exportar-csv")]
    public async Task<IActionResult> ExportarCsv()
    {
        var result = await svc.ExportarAsync();
        if (!result.Success) return BadRequest();

        var sb = new StringBuilder();
        sb.AppendLine("SKU,Produto,Variação,Quantidade,Status,Validade,Lote,Última Movimentação");
        foreach (var item in result.Data!.Data)
        {
            sb.AppendLine(string.Join(",",
                Csv(item.Sku),
                Csv(item.Produto?.Nome),
                Csv(item.Variacao?.Nome),
                item.Qty,
                Csv(item.Status),
                item.Validade?.ToString("yyyy-MM-dd") ?? "",
                Csv(item.Lote),
                item.LastMov.ToString("yyyy-MM-dd")));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"estoque-{BrazilTime.Now():yyyyMMdd}.csv");
    }

    private static string Csv(string? value) =>
        value is null ? "" : $"\"{value.Replace("\"", "\"\"").Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ")}\"";


    [HttpGet("/estoque/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Item de Estoque";
        ViewBag.ActiveMenuItem = "Estoque";

        var result = await svc.ObterAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        var item = result.Data!;
        var vm = new EstoqueDetailViewModel
        {
            Item = item,
            Produto = item.Produto,
            Variacao = item.Variacao
        };
        return View(vm);
    }

    [HttpPost("/estoque/saida")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickSaida([FromBody] QuickSaidaRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EstoqueId) || req.Qty < 1)
            return BadRequest(new { success = false, errorMessage = "Dados inválidos. Verifique o item e a quantidade." });

        var itemResult = await svc.ObterAsync(req.EstoqueId);
        if (!itemResult.Success || itemResult.Data is null)
            return BadRequest(new { success = false, errorMessage = "Item de estoque não encontrado." });

        var item = itemResult.Data;

        if (!DateOnly.TryParse(req.Data, out var data))
            data = BrazilTime.Today();

        // Saída rápida a partir da listagem de estoque é sempre de um lote específico
        // (o que o usuário clicou). ItemEstoqueId força a API a consumir esse lote
        // em vez de cair na rota FIFO/FEFO por ProdutoId.
        var natureza = (req.Natureza ?? "venda").Trim().ToLowerInvariant();

        // Validacoes amigaveis no Web — evita rodada API/erro genérico.
        if (natureza == "venda" && (!req.Valor.HasValue || req.Valor.Value <= 0))
            return BadRequest(new { success = false, errorMessage = "Para Venda, informe o valor unitário." });

        var motivoNorm = string.IsNullOrWhiteSpace(req.Motivo) ? null : req.Motivo.Trim();
        if ((natureza == "perda" || natureza == "prejuizo") && motivoNorm is null)
            return BadRequest(new { success = false, errorMessage = "Informe o motivo da saída para auditoria." });

        // Politica "operacao nao trava": se estoque insuficiente, repoe automaticamente
        // o lote clicado com a diferenca, mantem trilha de auditoria com observacao
        // padronizada e segue com a saida. Operador corrige depois — ver req do Felipe.
        bool ajusteAutomatico = false;
        int qtyAjuste = 0;
        if (req.Qty > item.Qty)
        {
            qtyAjuste = req.Qty - item.Qty;
            var custoUnitario = item.CustoUnitario?.Valor > 0 ? item.CustoUnitario.Valor : 0.01m;
            var reposicaoVm = new ReposicaoFormViewModel
            {
                ItemEstoqueId = req.EstoqueId,
                ProdutoId = item.ProdutoId,
                Qty = qtyAjuste,
                Custo = custoUnitario,
                Data = data,
                Validade = item.Validade,
                Observacoes = $"Ajuste automatico: saida solicitou {req.Qty}un mas estoque tinha {item.Qty}un. Operador deve revisar."
            };
            var ajusteResult = await entradasSvc.ReposicaoAsync(reposicaoVm);
            if (!ajusteResult.Success)
            {
                log.LogWarning("Ajuste automatico FALHOU em /estoque/saida. itemId={ItemId} produtoId={ProdutoId} solicitado={Solicitado} disponivel={Disponivel} erro={Erro}",
                    req.EstoqueId, item.ProdutoId, req.Qty, item.Qty, ajusteResult.ErrorMessage);
                return BadRequest(new
                {
                    success = false,
                    errorMessage = $"Estoque insuficiente ({item.Qty}un disponiveis) e nao foi possivel ajustar automaticamente: {ajusteResult.ErrorMessage ?? "erro desconhecido"}."
                });
            }
            ajusteAutomatico = true;
            log.LogWarning("Ajuste automatico aplicado em /estoque/saida. itemId={ItemId} produtoId={ProdutoId} reposicao={Ajuste}un (de {Disponivel} para {Total}). Saida natureza={Natureza} qty={Qty}.",
                req.EstoqueId, item.ProdutoId, qtyAjuste, item.Qty, req.Qty, natureza, req.Qty);
        }

        var saidaVm = new SaidaFormViewModel
        {
            ItemEstoqueId = req.EstoqueId,
            ProdutoId = item.ProdutoId,
            VarId = item.VarId,
            Natureza = natureza,
            Qty = req.Qty,
            Valor = req.Valor,
            DtVenda = data,
            Descricao = motivoNorm
        };

        var result = await saidasSvc.CriarAsync(saidaVm);
        if (!result.Success)
            return BadRequest(new { success = false, errorMessage = result.ErrorMessage ?? "Erro ao registrar saída." });

        return Ok(new
        {
            success = true,
            ajusteAutomatico,
            qtyAjuste,
            mensagemAjuste = ajusteAutomatico
                ? $"Estoque tinha {item.Qty}un, foram repostas {qtyAjuste}un automaticamente. Revise o cadastro depois."
                : null
        });
    }

    [HttpGet("/estoque/produto-detalhe/{id}")]
    public async Task<IActionResult> ProdutoDetalhe(string id)
    {
        var result = await svc.ObterProdutoDetalheAsync(id);
        if (!result.Success || result.Data is null)
            return NotFound(new { error = "Produto não encontrado." });

        var p = result.Data;
        return Json(new
        {
            id = p.ProdutoId,
            nome = p.Nome,
            sku = p.SkuBase,
            codigoBarras = p.CodigoBarras,
            marca = p.Marca,
            fotoUrl = p.Fotos.FirstOrDefault()?.Url,
            estoqueTotal = p.QuantidadeTotalEstoque,
            custoReferencia = p.CustoReferencia,
            precoReferencia = p.PrecoReferencia,
            controlaValidade = p.ControlaValidade,
            margemEstimada = p.MargemEstimada,
            ultimaEntradaEm = p.UltimaEntradaEm,
            criadoPorNome = p.CriadoPorNome,
            alteradoPorNome = p.AlteradoPorNome,
            variacoes = p.Variacoes
                .Where(v => v.Ativa)
                .Select(v => new
                {
                    id = v.VariacaoId,
                    nome = v.Nome,
                    sku = v.Sku,
                    codigoBarras = v.CodigoBarras,
                    quantidadeEmEstoque = v.QuantidadeEmEstoque,
                    ultimaEntradaEm = v.UltimaEntradaEm
                })
        });
    }

    [HttpGet("/estoque/itens-por-produto/{produtoId}")]
    public async Task<IActionResult> ItensPorProduto(string produtoId)
    {
        var result = await svc.ObterItensPorProdutoAsync(produtoId);
        if (!result.Success) return Json(Array.Empty<object>());
        return Json(result.Data);
    }
}
