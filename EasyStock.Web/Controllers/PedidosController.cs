using EasyStock.Web.Helpers;
using EasyStock.Web.Models.ViewModels.Pedidos;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class CriarPedidoWebRequest
{
    public Guid? ClienteId { get; set; }
    public string? NomeAdHoc { get; set; }
    public string? AptAdHoc { get; set; }
    public string? TelefoneAdHoc { get; set; }
    public string? Observacoes { get; set; }
    public List<CriarItemInput>? Itens { get; set; }
    public DateTime? AgendadoParaEm { get; set; }
}

/// <summary>Venda balcao: cliente + itens (com produto novo opcional) + pagamento, finalizado atomico.</summary>
public class CriarBalcaoWebRequest
{
    public Guid? ClienteId { get; set; }
    public string? NovoClienteNome { get; set; }
    public string? NovoClienteApt { get; set; }
    public string? NovoClienteTelefone { get; set; }
    public string? NomeAdHoc { get; set; }
    public List<BalcaoItemInput>? Itens { get; set; }
    public bool Pagou { get; set; }
    public string? FormaPagamento { get; set; }
    public string? Observacoes { get; set; }
}

public class BalcaoItemInput
{
    public string Nome { get; set; } = "";
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public Guid? ProdutoId { get; set; }
    public bool NovoProduto { get; set; }
    public Guid? CategoriaId { get; set; }
    public decimal? CustoReferencia { get; set; }
}

public class PedidosController(
    PedidosService svc,
    ClientesService clientesSvc,
    ProdutosService produtosSvc,
    TicketsApiService ticketsSvc,
    SessionService session) : BaseController(session)
{
    [HttpGet("/pedidos/novo")]
    public IActionResult Novo() => RedirectToAction(nameof(Index));

    [HttpGet("/pedidos")]
    public async Task<IActionResult> Index(string? search = null, string? status = null)
    {
        ViewBag.Title = "Pedidos";
        ViewBag.ActiveMenuItem = "Pedidos";

        var vm = new PedidosListViewModel { Search = search, FiltroStatus = status };

        var result = await svc.ListarAsync(status, search: search);
        if (result.Success && result.Data is not null) vm.Items = result.Data;

        var cli = await clientesSvc.ListarAsync(status: "ativo");
        if (cli.Success && cli.Data is not null) vm.Clientes = cli.Data;

        // Categorias para o cadastro rápido de produto inline no modal Novo pedido.
        var cats = await produtosSvc.ListarCategoriasAsync();
        if (cats.Success && cats.Data is not null) vm.Categorias = cats.Data;

        return View(vm);
    }

    [HttpGet("/pedidos/{id:guid}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Pedido";
        ViewBag.ActiveMenuItem = "Pedidos";

        var result = await svc.ObterAsync(id);
        if (HasError(result) || result.Data is null) return RedirectToAction(nameof(Index));

        return View(new PedidoDetailViewModel { Detalhe = result.Data });
    }

    /// <summary>
    /// Onda P5.C — Recibo print-friendly. View renderiza HTML otimizado pra
    /// CTRL+P → "Salvar como PDF" do browser. Sem dependência de lib de PDF.
    /// </summary>
    [HttpGet("/pedidos/{id}/recibo")]
    public async Task<IActionResult> Recibo(string id)
    {
        var result = await svc.ObterAsync(id);
        if (HasError(result) || result.Data is null) return RedirectToAction(nameof(Detail), new { id });

        ViewBag.Title = "Recibo";
        return View(new PedidoDetailViewModel { Detalhe = result.Data });
    }

    [HttpPost("/pedidos/json")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarJson([FromBody] CriarPedidoWebRequest req)
    {
        if (req.ClienteId == null && string.IsNullOrWhiteSpace(req.NomeAdHoc))
            return BadRequest(new
            {
                success = false,
                error = new { code = "VALIDATION_ERROR", message = "Informe um cliente OU um nome ad-hoc." }
            });

        var result = await svc.CriarAsync(req.ClienteId, req.NomeAdHoc, req.AptAdHoc, req.TelefoneAdHoc,
            req.Observacoes, req.Itens, req.AgendadoParaEm);
        if (!result.Success)
            return StatusCode(result.HttpStatus > 0 ? result.HttpStatus : 400, new
            {
                success = false,
                error = new
                {
                    code = result.ErrorCode ?? "API_ERROR",
                    message = result.ErrorMessage ?? "Erro ao criar pedido."
                }
            });

        return Ok(new { success = true, id = result.Data?.Id });
    }

    [HttpPost("/pedidos/balcao.json")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarBalcaoJson([FromBody] CriarBalcaoWebRequest req)
    {
        if (req.Itens == null || req.Itens.Count == 0)
            return BadRequest(new
            {
                success = false,
                error = new { code = "VALIDATION_ERROR", message = "Adicione pelo menos 1 item ao pedido." }
            });

        var result = await svc.FinalizarBalcaoAsync(req);
        if (!result.Success)
            return StatusCode(result.HttpStatus > 0 ? result.HttpStatus : 400, new
            {
                success = false,
                error = new
                {
                    code = result.ErrorCode ?? "API_ERROR",
                    message = result.ErrorMessage ?? "Erro ao finalizar a venda balcão."
                }
            });

        return Ok(new { success = true, id = result.Data?.PedidoId, pago = result.Data?.Pago, total = result.Data?.Total });
    }

    [HttpPost("/pedidos/{id}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarStatus(string id, string status)
    {
        var result = await svc.AtualizarStatusAsync(id, status);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", $"Pedido marcado como {status}.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/pedidos/{id}/agendar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Agendar(string id, DateTime? agendadoParaEm)
    {
        var result = await svc.AlterarAgendamentoAsync(id, agendadoParaEm);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        var msg = agendadoParaEm.HasValue
            ? $"Pedido agendado para {agendadoParaEm.Value.ParaBrasilia():dd/MM/yyyy HH:mm}."
            : "Agendamento removido.";
        Toast("success", msg);
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/pedidos/{id}/cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(string id, string? motivo)
    {
        var result = await svc.CancelarAsync(id, motivo);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Pedido cancelado.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Onda 1.1 — abre ticket SaaS pra EasyStok reportando problema sobre
    /// um pedido especifico. Vincula via PedidoId (cross-tenant validado no
    /// use case). Cliente nao escolhe prioridade; categoria default Solicitacao.
    /// </summary>
    [HttpPost("/pedidos/{id:guid}/reportar-problema")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReportarProblema(Guid id, string motivo, string? descricao, string? categoria)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            Toast("error", "Informe o motivo da reclamacao.");
            return RedirectToAction(nameof(Detail), new { id });
        }

        var titulo = $"Pedido {id.ToString().Substring(0, 8)} — {motivo}";
        var desc = string.IsNullOrWhiteSpace(descricao) ? motivo : descricao;
        var cat = string.IsNullOrWhiteSpace(categoria) ? "Solicitacao" : categoria;

        var result = await ticketsSvc.AbrirAsync(titulo, desc, cat, pedidoId: id);
        if (HasError(result))
        {
            Toast("error", $"Falha ao abrir ticket: {result.ErrorMessage}");
            return RedirectToAction(nameof(Detail), new { id });
        }

        Toast("success", $"Ticket aberto. Acompanhe em Suporte.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/pedidos/{id}/itens")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(string id, string nome, decimal quantidade, decimal precoUnitario,
        Guid? produtoId, string? emoji, string? unidade, string? observacao)
    {
        var result = await svc.AdicionarItemAsync(id, nome, quantidade, precoUnitario,
            produtoId, emoji, unidade, observacao);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Item adicionado.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/pedidos/{id}/itens/{itemId}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(string id, string itemId)
    {
        var result = await svc.RemoverItemAsync(id, itemId);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Item removido.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/pedidos/{id}/pagamentos")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPagamento(string id, string metodo, decimal valor, string? referencia, string? observacao)
    {
        var result = await svc.RegistrarPagamentoAsync(id, metodo, valor, referencia, observacao);
        if (HasError(result))
        {
            Toast("error", result.ErrorMessage ?? "Não foi possível registrar o pagamento. Confira os dados e tente de novo.");
            return RedirectToAction(nameof(Detail), new { id, tab = "pagamentos" });
        }

        Toast("success", $"Pagamento de {valor:C} ({metodo}) registrado.");
        return RedirectToAction(nameof(Detail), new { id, tab = "pagamentos" });
    }

    [HttpPost("/pedidos/{id}/pagamentos/{pagamentoId}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePagamento(string id, string pagamentoId)
    {
        var result = await svc.RemoverPagamentoAsync(id, pagamentoId);
        if (HasError(result))
        {
            Toast("error", result.ErrorMessage ?? "Não foi possível remover o pagamento.");
            return RedirectToAction(nameof(Detail), new { id, tab = "pagamentos" });
        }

        Toast("success", "Pagamento estornado do pedido.");
        return RedirectToAction(nameof(Detail), new { id, tab = "pagamentos" });
    }

    /// <summary>
    /// Ultimo pedido NAO cancelado do cliente — usado pelo modal Novo pedido pro
    /// atalho "Repetir ultimo". Retorna {found:false} quando o cliente nao tem
    /// historico ou todos os pedidos estao cancelados.
    /// </summary>
    [HttpGet("/pedidos/cliente/{clienteId}/ultimo")]
    public async Task<IActionResult> UltimoDoCliente(Guid clienteId)
    {
        if (clienteId == Guid.Empty)
            return Json(new { found = false });

        var lista = await svc.ListarAsync(clienteId: clienteId);
        if (!lista.Success || lista.Data is null || lista.Data.Count == 0)
            return Json(new { found = false });

        var ultimo = lista.Data
            .Where(p => !string.Equals(p.Status, "cancelado", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.CriadoEm)
            .FirstOrDefault();
        if (ultimo is null)
            return Json(new { found = false });

        var detalhe = await svc.ObterAsync(ultimo.Id);
        if (!detalhe.Success || detalhe.Data is null)
            return Json(new { found = false });

        return Json(new
        {
            found = true,
            pedidoId = ultimo.Id,
            criadoEm = ultimo.CriadoEm,
            total = ultimo.Total,
            itensCount = ultimo.ItensCount,
            itens = detalhe.Data.Itens.Select(i => new
            {
                nome = i.Nome,
                quantidade = i.Quantidade,
                precoUnitario = i.PrecoUnitario,
                produtoId = i.ProdutoId
            })
        });
    }
}
