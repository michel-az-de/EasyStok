using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Dashboard;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class DashboardController(ApiClient api, SessionService session) : BaseController(session)
{
    [HttpGet("/dashboard")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Dashboard";
        ViewBag.ActiveMenuItem = "Dashboard";

        var lojas = await api.GetAsync<List<LojaApi>>("lojas");
        ViewBag.Lojas = lojas.Success && lojas.Data is not null ? lojas.Data : new List<LojaApi>();

        var vm = new DashboardViewModel();

        var dashTask = api.GetAsync<DashboardResumoApi>("analytics/dashboard");
        var diaTask = api.GetAsync<ResumoDiaApi>("analytics/dia");
        var reposTask = api.GetAsync<List<ReposicaoSugerida>>("analytics/reposicao");
        var movsTask = api.GetAsync<List<MovimentacaoResumo>>("analytics/movimentacoes?diasPadrao=30");
        var receitaTask = api.GetAsync<List<ReceitaPorPeriodoApi>>("analytics/receita?meses=6");
        var iaUsoTask = api.GetAsync<IaUsoApi>("ia/uso");

        var dashResult = await dashTask;
        var diaResult = await diaTask;
        var reposResult = await reposTask;
        var movsResult = await movsTask;
        var receitaResult = await receitaTask;
        var iaUsoResult = await iaUsoTask;

        if (dashResult.Success && dashResult.Data is { } d)
        {
            vm.TotalProdutos = d.TotalSkus;
            vm.QuantidadeTotalEmEstoque = d.QuantidadeTotalEmEstoque;
            vm.ValorEstoque = d.ValorTotalEstoque;
            vm.ReceitaMes = d.ReceitaEstimadaPeriodo;
            vm.MediaVendasDiaria = d.MediaVendasDiaria;
            vm.EstoqueCritico = d.AlertasEstoqueBaixo;
            vm.ProximosVencimento = d.AlertasVencimento;
            vm.ProdutosParados = d.AlertasItensParados;
        }

        if (diaResult.Success && diaResult.Data is { } dia)
        {
            vm.PedidosEntreguesHoje = dia.PedidosEntreguesHoje;
            vm.FaturamentoHoje = dia.FaturamentoHoje;
            vm.TicketMedioHoje = dia.TicketMedioHoje;
            vm.PedidosPendentes = dia.PedidosPendentes;
            vm.ValorPedidosPendentes = dia.ValorPedidosPendentes;
            vm.CaixaAbertaHoje = dia.CaixaAbertaHoje;
            vm.CaixaFechadaHoje = dia.CaixaFechadaHoje;
            vm.SaldoCaixaAtual = dia.SaldoCaixaAtual;
            vm.PixRecebidosHoje = dia.PixRecebidosHoje;
            vm.ValorPixHoje = dia.ValorPixHoje;
            vm.OnboardingCompleto = dia.OnboardingCompleto;
        }

        if (reposResult.Success && reposResult.Data is { } repos)
            vm.SugestoesReposicao = repos.Count;

        if (movsResult.Success && movsResult.Data is { } movs)
        {
            foreach (var m in movs.OrderByDescending(x => x.Ano).ThenByDescending(x => x.Mes).ThenByDescending(x => x.Dia).Take(10))
            {
                vm.MovimentacoesRecentes.Add(new MovimentacaoRecente
                {
                    Tipo = m.Tipo,
                    TotalMovimentacoes = m.TotalMovimentacoes,
                    Qty = m.QuantidadeTotal,
                    Valor = m.ValorTotal > 0 ? m.ValorTotal : null,
                    Data = new DateOnly(m.Ano, m.Mes, m.Dia)
                });
            }
        }

        if (receitaResult.Success && receitaResult.Data is { } receita)
        {
            var ordenado = receita.OrderBy(r => r.Ano).ThenBy(r => r.Mes).ToList();
            vm.GraficoLabels = ordenado.Select(r => $"{r.Mes:D2}/{r.Ano}").ToList();
            vm.GraficoDados = ordenado.Select(r => r.ReceitaBruta).ToList();

            if (ordenado.Count >= 2)
            {
                vm.ReceitaMesAtual = ordenado[^1].ReceitaBruta;
                vm.ReceitaMesAnterior = ordenado[^2].ReceitaBruta;
            }
            else if (ordenado.Count == 1)
            {
                vm.ReceitaMesAtual = ordenado[0].ReceitaBruta;
            }
        }

        if (iaUsoResult.Success && iaUsoResult.Data is { } ia)
        {
            vm.IaConfigurada = true;
            vm.IaIlimitada = ia.Ilimitado;
            vm.GeracoesIaUsadas = ia.TotalGeracoes;
            vm.GeracoesIaLimite = ia.LimiteMensal ?? 0;
        }

        vm.ResumoApiFalhou = !dashResult.Success;
        vm.TodasApisFalharam = !dashResult.Success
            && !reposResult.Success
            && !movsResult.Success
            && !receitaResult.Success;

        return View(vm);
    }

    [HttpGet("/dashboard/alertas")]
    public async Task<IActionResult> Alertas(Guid? lojaId = null, int page = 1, int pageSize = 30)
    {
        var tasks = new[]
        {
            api.GetAsync<object>($"analytics/validade?lojaId={lojaId}&page={page}&pageSize={pageSize}"),
            api.GetAsync<object>($"analytics/parados?lojaId={lojaId}&page={page}&pageSize={pageSize}"),
        };
        await Task.WhenAll(tasks);
        return Json(new
        {
            validade = tasks[0].Result.Success ? tasks[0].Result.Data : null,
            parados  = tasks[1].Result.Success ? tasks[1].Result.Data : null,
        });
    }

    [HttpGet("/dashboard/receita-custo")]
    public async Task<IActionResult> ReceitaCusto(int periodo = 30, Guid? lojaId = null, int tz = 0)
    {
        var result = await api.GetAsync<object>(
            $"analytics/receita-custo?periodo={periodo}&lojaId={lojaId}&tz={tz}");

        if (!result.Success)
            return StatusCode(502, new { message = result.ErrorMessage ?? "Erro ao carregar dados de receita." });

        return Json(result.Data);
    }

    [HttpGet("/dashboard/data")]
    public async Task<IActionResult> Data(int periodo = 30, Guid? lojaId = null, int tz = 0)
    {
        var result = await api.GetAsync<object>(
            $"analytics/dashboard-full?periodo={periodo}&lojaId={lojaId}&tz={tz}");

        if (!result.Success)
            return StatusCode(502, new { message = result.ErrorMessage ?? "Erro ao carregar dashboard." });

        return Json(result.Data);
    }

    [HttpGet("/dashboard/extras")]
    public async Task<IActionResult> Extras(int periodo = 30, Guid? lojaId = null, int tz = 0)
    {
        var result = await api.GetAsync<object>(
            $"analytics/dashboard-extras?periodo={periodo}&lojaId={lojaId}&tz={tz}");

        if (!result.Success)
            return StatusCode(502, new { message = result.ErrorMessage ?? "Erro ao carregar dados extras." });

        return Json(result.Data);
    }

    [HttpGet("/dashboard/pedido/{id}")]
    public async Task<IActionResult> PedidoDetalhe(Guid id)
    {
        var result = await api.GetAsync<object>($"pedidos/{id}");

        if (!result.Success)
            return StatusCode(502, new { message = result.ErrorMessage ?? "Pedido não encontrado." });

        return Json(result.Data);
    }
}
