using EasyStock.Web.Models.ViewModels.InteligenciaLojas;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class InteligenciaLojasController(InteligenciaLojasService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/inteligencia-lojas")]
    public async Task<IActionResult> Index([FromQuery] int periodo = 30)
    {
        ViewBag.Title = "Inteligência por Loja";
        ViewBag.ActiveMenuItem = "InteligenciaLojas";

        var vm = new InteligenciaLojasOverviewViewModel { PeriodoDias = periodo };

        var (comparacaoTask, indicadoresTask) = (
            svc.ComparacaoAsync(periodo),
            svc.IndicadoresAsync(periodo)
        );
        var (comparacaoResult, indicadoresResult) = (await comparacaoTask, await indicadoresTask);

        if (comparacaoResult.Success)
            vm.Lojas = comparacaoResult.Data ?? [];
        if (indicadoresResult.Success)
            vm.Indicadores = indicadoresResult.Data ?? [];

        return View(vm);
    }

    [HttpGet("/inteligencia-lojas/{lojaId:guid}")]
    public async Task<IActionResult> Detalhe(Guid lojaId, [FromQuery] int periodo = 30)
    {
        ViewBag.Title = "Detalhe da Loja";
        ViewBag.ActiveMenuItem = "InteligenciaLojas";

        var vm = new InteligenciaLojaDetalheViewModel { PeriodoDias = periodo };

        var (resumoTask, topTask, bottomTask, validadeTask, reposTask, indicadoresTask) = (
            svc.ResumoLojaAsync(lojaId, periodo),
            svc.TopProdutosAsync(lojaId, periodo),
            svc.BottomProdutosAsync(lojaId, periodo),
            svc.AlertasValidadeAsync(lojaId),
            svc.ReposicoesAsync(lojaId),
            svc.IndicadoresAsync(periodo, lojaId)
        );

        var resumoResult = await resumoTask;
        var topResult = await topTask;
        var bottomResult = await bottomTask;
        var validadeResult = await validadeTask;
        var reposResult = await reposTask;
        var indicadoresResult = await indicadoresTask;

        if (resumoResult.Success && resumoResult.Data is not null)
            vm.Resumo = resumoResult.Data;
        if (topResult.Success)
            vm.TopProdutos = topResult.Data ?? [];
        if (bottomResult.Success)
            vm.BottomProdutos = bottomResult.Data ?? [];
        if (validadeResult.Success)
            vm.AlertasValidade = validadeResult.Data ?? [];
        if (reposResult.Success)
            vm.Reposicoes = reposResult.Data ?? [];
        if (indicadoresResult.Success)
            vm.Indicadores = indicadoresResult.Data ?? [];

        return View(vm);
    }
}
