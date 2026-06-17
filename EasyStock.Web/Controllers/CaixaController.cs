using EasyStock.Web.Helpers;
using EasyStock.Web.Models.ViewModels.Caixa;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class CaixaController(CaixaService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/caixa")]
    public async Task<IActionResult> Index(DateOnly? data = null)
    {
        ViewBag.Title = "Caixa";
        ViewBag.ActiveMenuItem = "Caixa";
        // Servidor roda UTC (Render). Em janela 21–23h BRT, DateTime.Now retorna
        // o dia seguinte e o Caixa abria a tela errada — usar BrazilTime.Today().
        var d = data ?? BrazilTime.Today();
        var vm = new CaixaOperacaoViewModel { DataSelecionada = d };

        var result = await svc.ObterDiaAsync(d);
        if (result.Success && result.Data is not null)
        {
            vm.Dia = result.Data;
        }
        else
        {
            vm.ErrorCode = result.ErrorCode;
            vm.ErrorMessage = result.ErrorMessage;
        }

        return View(vm);
    }

    [HttpPost("/caixa/abrir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Abrir(decimal saldoInicial, string? observacoes)
    {
        var result = await svc.AbrirAsync(saldoInicial, observacoes);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", $"Caixa aberto com saldo inicial {saldoInicial:C}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/caixa/fechar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Fechar(string? observacoes)
    {
        // Não enviar data: o servidor é autoritativo e fecha a sessão aberta (que pode ser de
        // um dia anterior), datando o snapshot no dia da abertura — libera o caixa de hoje (#640).
        var result = await svc.FecharAsync(null, observacoes);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        var fech = result.Data;
        // Toast usa a data efetivamente fechada pelo servidor (não um palpite do cliente).
        // Toast() grava uma única chave em TempData → emitir um só toast (aviso engloba sucesso).
        var temAviso = fech?.Observacoes?.Contains("nao foram incluidos", StringComparison.OrdinalIgnoreCase) == true;
        if (temAviso)
            Toast("warning", $"Caixa de {fech?.Data:dd/MM/yyyy} fechado (saldo {fech?.SaldoFinal:C}). " +
                "Atenção: há lançamentos em dias anteriores não incluídos — revise no histórico.");
        else
            Toast("success", $"Caixa de {fech?.Data:dd/MM/yyyy} fechado. Saldo final: {fech?.SaldoFinal:C}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/caixa/movimentos")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarMovimento(
        string tipo, decimal valor, string? descricao,
        string? metodo, string? categoria, string? referencia, DateTime? dataMovimento)
    {
        var result = await svc.RegistrarMovimentoAsync(tipo, valor, descricao, metodo, categoria, referencia, dataMovimento);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", $"{(tipo == "entrada" ? "Entrada" : "Saída")} de {valor:C} registrada.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/caixa/movimentos/{id}/estornar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Estornar(string id, string? motivo)
    {
        var result = await svc.EstornarAsync(id, motivo);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Movimento estornado.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/caixa/historico")]
    public async Task<IActionResult> Historico()
    {
        ViewBag.Title = "Histórico de fechamentos";
        ViewBag.ActiveMenuItem = "Caixa";
        var vm = new CaixaHistoricoViewModel();
        var result = await svc.ListarFechamentosAsync();
        if (result.Success && result.Data is not null) vm.Fechamentos = result.Data;
        return View(vm);
    }
}
