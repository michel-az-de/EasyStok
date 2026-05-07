using EasyStock.Web.Models.ViewModels.Financeiro;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class ContasAReceberController(FinanceiroService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/contas-a-receber")]
    public async Task<IActionResult> Index(
        string? status = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        string? busca = null,
        int page = 1)
    {
        ViewBag.Title = "Contas a Receber";
        ViewBag.ActiveMenuItem = "ContasAReceber";

        var vm = new ContasReceberIndexViewModel
        {
            FiltroStatus = status,
            Busca = busca,
            VencimentoDe = vencimentoDe,
            VencimentoAte = vencimentoAte
        };

        var lista = await svc.ListarContasReceberAsync(status, vencimentoDe, vencimentoAte, busca, page);
        if (lista.Success && lista.Data is not null) vm.Resultado = lista.Data;
        else if (lista.ErrorMessage is not null) Toast("error", lista.ErrorMessage);

        return View(vm);
    }

    [HttpGet("/contas-a-receber/criar")]
    public async Task<IActionResult> Criar()
    {
        ViewBag.Title = "Nova Conta a Receber";
        ViewBag.ActiveMenuItem = "ContasAReceber";

        var vm = new CriarContaViewModel();
        var cats = await svc.ListarCategoriasAsync(ativa: true, tipo: "Receita");
        if (cats.Success && cats.Data is not null) vm.Categorias = cats.Data;
        var centros = await svc.ListarCentrosCustoAsync(ativo: true);
        if (centros.Success && centros.Data is not null) vm.CentrosCusto = centros.Data;
        return View(vm);
    }

    [HttpPost("/contas-a-receber/criar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarPost(
        string descricao,
        Guid categoriaFinanceiraId,
        DateTime dataEmissao,
        decimal valorTotal,
        int numeroParcelas,
        DateTime primeiraVencimento,
        string intervaloTipo = "mensal",
        Guid? clienteId = null,
        Guid? centroCustoId = null,
        string? observacoes = null,
        bool emitirAposCriar = false)
    {
        if (string.IsNullOrWhiteSpace(descricao))
        {
            Toast("error", "Descricao e obrigatoria.");
            return RedirectToAction(nameof(Criar));
        }
        if (numeroParcelas < 1 || numeroParcelas > 36)
        {
            Toast("error", "Numero de parcelas deve estar entre 1 e 36.");
            return RedirectToAction(nameof(Criar));
        }
        if (valorTotal <= 0m)
        {
            Toast("error", "Valor total deve ser positivo.");
            return RedirectToAction(nameof(Criar));
        }

        var parcelas = MontarParcelas(valorTotal, numeroParcelas, primeiraVencimento, intervaloTipo);

        var result = await svc.CriarContaReceberAsync(
            descricao, categoriaFinanceiraId, dataEmissao, parcelas,
            clienteId, centroCustoId, observacoes, emitirAposCriar);

        if (HasError(result) || result.Data is null) return RedirectToAction(nameof(Criar));

        Toast("success", "Conta a receber criada com sucesso.");
        return RedirectToAction(nameof(Detalhe), new { id = result.Data.Id });
    }

    [HttpGet("/contas-a-receber/{id:guid}")]
    public async Task<IActionResult> Detalhe(Guid id)
    {
        ViewBag.Title = "Detalhes da Conta a Receber";
        ViewBag.ActiveMenuItem = "ContasAReceber";

        var result = await svc.ObterContaReceberAsync(id);
        if (HasError(result) || result.Data is null)
            return RedirectToAction(nameof(Index));

        return View(new ContaReceberDetalheViewModel { Conta = result.Data });
    }

    [HttpPost("/contas-a-receber/{id:guid}/emitir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Emitir(Guid id)
    {
        var result = await svc.EmitirContaReceberAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Detalhe), new { id });
        Toast("success", "Conta emitida.");
        return RedirectToAction(nameof(Detalhe), new { id });
    }

    [HttpPost("/contas-a-receber/{id:guid}/cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(Guid id, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            Toast("error", "Motivo e obrigatorio.");
            return RedirectToAction(nameof(Detalhe), new { id });
        }
        var result = await svc.CancelarContaReceberAsync(id, motivo);
        if (HasError(result)) return RedirectToAction(nameof(Detalhe), new { id });
        Toast("success", "Conta cancelada.");
        return RedirectToAction(nameof(Detalhe), new { id });
    }

    private static List<object> MontarParcelas(decimal valorTotal, int n, DateTime primeira, string intervalo)
    {
        var lista = new List<object>(n);
        var valorParcela = Math.Round(valorTotal / n, 2, MidpointRounding.AwayFromZero);
        var residuo = valorTotal - valorParcela * n;
        var dataAtual = primeira;
        for (var i = 1; i <= n; i++)
        {
            var v = i == n ? valorParcela + residuo : valorParcela;
            lista.Add(new
            {
                numero = i,
                valor = v,
                dataVencimento = dataAtual,
                metodoPlanejado = (string?)null
            });
            dataAtual = intervalo switch
            {
                "quinzenal" => dataAtual.AddDays(15),
                "semanal" => dataAtual.AddDays(7),
                _ => dataAtual.AddMonths(1)
            };
        }
        return lista;
    }
}
