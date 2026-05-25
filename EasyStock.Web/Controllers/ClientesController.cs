using EasyStock.Web.Models.ViewModels.Clientes;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class CriarClienteWebRequest
{
    public string Nome { get; set; } = "";
    public string? Apt { get; set; }
    public string? Endereco { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Documento { get; set; }
    public string? Observacoes { get; set; }
}

public class ClientesController(ClientesService svc, SessionService session) : BaseController(session)
{
    private const int SearchResultLimit = 5;
    [HttpGet("/clientes")]
    public async Task<IActionResult> Index(string? search = null, string? status = null)
    {
        ViewBag.Title = "Clientes";
        ViewBag.ActiveMenuItem = "Clientes";

        var result = await svc.ListarAsync(status, search);
        var vm = new ClientesListViewModel { Search = search, FiltroStatus = status };
        if (result.Success && result.Data is not null) vm.Items = result.Data;
        return View(vm);
    }

    [HttpGet("/clientes/novo")]
    public IActionResult Novo() => RedirectToAction(nameof(Index));

    [HttpGet("/clientes/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Cliente";
        ViewBag.ActiveMenuItem = "Clientes";

        var result = await svc.ObterAsync(id);
        if (HasError(result) || result.Data is null) return RedirectToAction(nameof(Index));

        return View(new ClienteDetailViewModel { Detalhe = result.Data });
    }

    [HttpPost("/clientes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(
        string nome, string? apt, string? endereco, string? telefone,
        string? email, string? documento, string? observacoes)
    {
        var result = await svc.CriarAsync(nome, apt, endereco, telefone, email, documento, observacoes);
        if (HasErrorVerbose(result, "Criar cliente")) return RedirectToAction(nameof(Index));

        Toast("success", "Cliente criado com sucesso!");
        return RedirectToAction(nameof(Detail), new { id = result.Data?.Id });
    }

    [HttpGet("/clientes/buscar-json")]
    public async Task<IActionResult> BuscarJson(string? q = null, int limit = SearchResultLimit)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<object>());
        var result = await svc.ListarAsync(null, q);
        if (!result.Success || result.Data is null) return Ok(Array.Empty<object>());
        return Ok(result.Data.Take(limit).Select(c => new { id = c.Id, nome = c.Nome, telefone = c.Telefone }));
    }

    [HttpPost("/clientes/json")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarJson([FromBody] CriarClienteWebRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Nome))
            return BadRequest(new
            {
                success = false,
                error = new { code = "VALIDATION_ERROR", message = "Informe o nome do cliente." }
            });

        var result = await svc.CriarAsync(req.Nome, req.Apt, req.Endereco, req.Telefone,
            req.Email, req.Documento, req.Observacoes);

        if (!result.Success)
            return StatusCode(result.HttpStatus > 0 ? result.HttpStatus : 400, new
            {
                success = false,
                error = new
                {
                    code = result.ErrorCode ?? "API_ERROR",
                    message = result.ErrorMessage ?? "Erro ao criar cliente."
                }
            });

        return Ok(new { success = true, id = result.Data?.Id });
    }

    [HttpPost("/clientes/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(string id,
        string nome, string? apt, string? endereco, string? telefone,
        string? email, string? documento, string? observacoes)
    {
        var result = await svc.EditarAsync(id, nome, apt, endereco, telefone, email, documento, observacoes);
        if (HasErrorVerbose(result, "Editar cliente")) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Cliente atualizado com sucesso!");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/clientes/{id}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Excluir(string id)
    {
        var result = await svc.ExcluirAsync(id);
        if (HasErrorVerbose(result, "Desativar cliente")) return RedirectToAction(nameof(Detail), new { id });

        Toast("success", "Cliente desativado.", $"/clientes/{id}/reativar");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/clientes/{id}/reativar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reativar(string id)
    {
        var result = await svc.ReativarAsync(id);
        if (HasErrorVerbose(result, "Reativar cliente")) return RedirectToAction(nameof(Index));

        Toast("success", "Cliente reativado.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    // ── Sub-recursos: endereços ────────────────────────────────────────

    [HttpPost("/clientes/{id}/enderecos")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddEndereco(string id,
        string? tipo, string? logradouro, string? numero, string? complemento,
        string? bairro, string? cidade, string? estado, string? cep, string? pais,
        string? referencia, bool padrao = false)
    {
        var result = await svc.AddEnderecoAsync(id, tipo, logradouro, numero, complemento,
            bairro, cidade, estado, cep, pais, referencia, padrao);
        if (HasErrorVerbose(result, "Adicionar endereço")) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Endereço adicionado.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/clientes/{id}/enderecos/{enderecoId}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveEndereco(string id, string enderecoId)
    {
        var result = await svc.RemoveEnderecoAsync(id, enderecoId);
        if (HasErrorVerbose(result, "Remover endereço")) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Endereço removido.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    // ── Sub-recursos: telefones ────────────────────────────────────────

    [HttpPost("/clientes/{id}/telefones")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTelefone(string id,
        string numero, string? tipo, bool whatsapp = false, bool principal = false, string? observacao = null)
    {
        var result = await svc.AddTelefoneAsync(id, numero, tipo, whatsapp, principal, observacao);
        if (HasErrorVerbose(result, "Adicionar telefone")) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Telefone adicionado.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/clientes/{id}/telefones/{telefoneId}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTelefone(string id, string telefoneId)
    {
        var result = await svc.RemoveTelefoneAsync(id, telefoneId);
        if (HasErrorVerbose(result, "Remover telefone")) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Telefone removido.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    // ── Sub-recursos: documentos ───────────────────────────────────────

    [HttpPost("/clientes/{id}/documentos")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDocumento(string id,
        string tipo, string valor, string? emissor, DateTime? emitidoEm, DateTime? validoAte, bool principal = false)
    {
        var result = await svc.AddDocumentoAsync(id, tipo, valor, emissor, emitidoEm, validoAte, principal);
        if (HasErrorVerbose(result, "Adicionar documento")) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Documento adicionado.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/clientes/{id}/documentos/{documentoId}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDocumento(string id, string documentoId)
    {
        var result = await svc.RemoveDocumentoAsync(id, documentoId);
        if (HasErrorVerbose(result, "Remover documento")) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Documento removido.");
        return RedirectToAction(nameof(Detail), new { id });
    }

}
