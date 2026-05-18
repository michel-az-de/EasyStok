using EasyStock.Web.Models.ViewModels.ConfiguracaoFiscal;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class ConfiguracaoFiscalController(
    ConfiguracaoFiscalService svc,
    SessionService session) : BaseController(session)
{
    [HttpGet("/configuracao-fiscal")]
    public async Task<IActionResult> Index()
    {
        ViewBag.ActiveMenuItem = "ConfiguracaoFiscal";
        ViewBag.Title = "Configuracao Fiscal";

        var result = await svc.ObterAsync();
        if (HasError(result)) return View(new ConfiguracaoFiscalViewModel());

        // Quando empresa nao tem config ainda, a API devolve { configurado = false } —
        // o ViewModel ja cobre esse caso (todos os campos opcionais sao null).
        return View(result.Data ?? new ConfiguracaoFiscalViewModel());
    }

    [HttpPost("/configuracao-fiscal/dados-emitente")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarDadosEmitente(
        string regimeTributario,
        string? inscricaoEstadual,
        string? inscricaoMunicipal,
        string? logradouro,
        string? numero,
        string? complemento,
        string? bairro,
        string? cidade,
        string? uf,
        string? cep)
    {
        var endereco = new EnderecoFiscalDto
        {
            Logradouro = logradouro,
            Numero = numero,
            Complemento = complemento,
            Bairro = bairro,
            Cidade = cidade,
            Uf = uf,
            Cep = cep,
        };

        var result = await svc.AtualizarDadosEmitenteAsync(
            regimeTributario, inscricaoEstadual, inscricaoMunicipal, endereco);

        if (HasErrorVerbose(result, "Dados do emitente"))
            return RedirectToAction(nameof(Index));

        Toast("success", "Dados do emitente atualizados.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/configuracao-fiscal/provedor")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarProvedor(string provedor)
    {
        if (string.IsNullOrWhiteSpace(provedor))
        {
            Toast("error", "Selecione um provedor.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.EscolherProvedorAsync(provedor);
        if (HasErrorVerbose(result, "Provedor"))
            return RedirectToAction(nameof(Index));

        Toast("success", $"Provedor '{provedor}' selecionado.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/configuracao-fiscal/csc")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarCsc(string cscId, string cscToken)
    {
        if (string.IsNullOrWhiteSpace(cscId) || string.IsNullOrWhiteSpace(cscToken))
        {
            Toast("error", "CSC ID e CSC Token sao obrigatorios.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.ConfigurarCscAsync(cscId, cscToken);
        if (HasErrorVerbose(result, "CSC"))
            return RedirectToAction(nameof(Index));

        Toast("success", "CSC configurado com sucesso.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/configuracao-fiscal/serie-ambiente")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarSerieAmbiente(string? ambiente, short? serieNfce)
    {
        var result = await svc.AlterarSerieAmbienteAsync(ambiente, serieNfce);
        if (HasErrorVerbose(result, "Serie/Ambiente"))
            return RedirectToAction(nameof(Index));

        Toast("success", "Serie e ambiente atualizados.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/configuracao-fiscal/habilitar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Habilitar()
    {
        var result = await svc.HabilitarAsync();
        if (HasErrorVerbose(result, "Habilitar emissao"))
            return RedirectToAction(nameof(Index));

        Toast("success", "Emissao fiscal habilitada.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/configuracao-fiscal/desabilitar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Desabilitar()
    {
        var result = await svc.DesabilitarAsync();
        if (HasErrorVerbose(result, "Desabilitar emissao"))
            return RedirectToAction(nameof(Index));

        Toast("success", "Emissao fiscal desabilitada.");
        return RedirectToAction(nameof(Index));
    }
}
