using EasyStock.Web.Models.ViewModels.Inteligencia;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class InteligenciaController(InteligenciaService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/inteligencia")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Inteligência Operacional";
        ViewBag.ActiveMenuItem = "Inteligencia";

        var vm = new InteligenciaViewModel();

        var boardTask     = svc.BoardAsync();
        var baixoTask     = svc.EstoqueBaixoAsync();
        var vencTask      = svc.ProximoVencimentoAsync();
        var paradoTask    = svc.ItensParadosAsync();
        var reposTask     = svc.SugestoesReposicaoAsync();
        var rupturaTask   = svc.ProjecaoRupturaAsync();

        await Task.WhenAll(boardTask, baixoTask, vencTask, paradoTask, reposTask, rupturaTask);

        var boardResult   = await boardTask;
        var baixoResult   = await baixoTask;
        var vencResult    = await vencTask;
        var paradoResult  = await paradoTask;
        var reposResult   = await reposTask;
        var rupturaResult = await rupturaTask;

        if (boardResult.Success && boardResult.Data is { } board)
        {
            vm.QuantidadeEmEstoque    = board.QuantidadeEmEstoque;
            vm.ValorTotalEstoque      = board.ValorTotalEstoque;
            vm.MediaVendasDiaria      = board.MediaVendasDiaria;
            vm.ProjecaoReceitaPeriodo = board.ProjecaoReceitaPeriodo;
        }
        else
        {
            vm.BoardLoadFailed = true;
        }

        if (baixoResult.Success && baixoResult.Data is { } baixo)
            vm.EstoqueBaixo = baixo;

        if (vencResult.Success && vencResult.Data is { } venc)
            vm.ProximoVencimento = venc;

        if (paradoResult.Success && paradoResult.Data is { } parado)
            vm.ItensParados = parado;

        if (reposResult.Success && reposResult.Data is { } repos)
            vm.SugestoesReposicao = repos;

        if (rupturaResult.Success && rupturaResult.Data is { } ruptura)
            vm.ProjecaoRuptura = ruptura;

        return View(vm);
    }
}
