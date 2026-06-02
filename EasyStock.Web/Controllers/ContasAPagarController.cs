using EasyStock.Web.Models.ViewModels.Financeiro;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class ContasAPagarController(FinanceiroService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/contas-a-pagar")]
    public async Task<IActionResult> Index(
        string? status = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        string? busca = null,
        int page = 1)
    {
        ViewBag.Title = "Contas a Pagar";
        ViewBag.ActiveMenuItem = "ContasAPagar";

        var vm = new ContasPagarIndexViewModel
        {
            FiltroStatus = status,
            Busca = busca,
            VencimentoDe = vencimentoDe,
            VencimentoAte = vencimentoAte
        };

        var lista = await svc.ListarContasPagarAsync(status, vencimentoDe, vencimentoAte, busca, page);
        if (lista.Success && lista.Data is not null) vm.Resultado = lista.Data;
        else if (lista.ErrorMessage is not null) Toast("error", lista.ErrorMessage);

        return View(vm);
    }

    [HttpGet("/contas-a-pagar/criar")]
    public async Task<IActionResult> Criar()
    {
        ViewBag.Title = "Nova Conta a Pagar";
        ViewBag.ActiveMenuItem = "ContasAPagar";

        var vm = new CriarContaViewModel();
        var cats = await svc.ListarCategoriasAsync(ativa: true, tipo: "Despesa");
        if (cats.Success && cats.Data is not null) vm.Categorias = cats.Data;
        var centros = await svc.ListarCentrosCustoAsync(ativo: true);
        if (centros.Success && centros.Data is not null) vm.CentrosCusto = centros.Data;
        return View(vm);
    }

    [HttpPost("/contas-a-pagar/criar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarPost(
        string descricao,
        Guid categoriaFinanceiraId,
        DateTime dataEmissao,
        decimal valorTotal,
        int numeroParcelas,
        DateTime primeiraVencimento,
        string intervaloTipo = "mensal",
        Guid? fornecedorId = null,
        Guid? centroCustoId = null,
        string? observacoes = null,
        bool emitirAposCriar = false)
    {
        // Re-renderiza o form preservando o que o usuario digitou (W-005/6: nao perder dados no erro).
        async Task<IActionResult> ReexibirComErro(string erro)
        {
            var vmErro = new CriarContaViewModel
            {
                Descricao = descricao,
                CategoriaFinanceiraId = categoriaFinanceiraId == Guid.Empty ? null : categoriaFinanceiraId,
                CentroCustoId = centroCustoId,
                DataEmissao = dataEmissao,
                ValorTotal = valorTotal,
                NumeroParcelas = numeroParcelas,
                PrimeiraVencimento = primeiraVencimento,
                IntervaloTipo = intervaloTipo,
                Observacoes = observacoes,
                EmitirAposCriar = emitirAposCriar,
                Erro = erro
            };
            var catsErro = await svc.ListarCategoriasAsync(ativa: true, tipo: "Despesa");
            if (catsErro.Success && catsErro.Data is not null) vmErro.Categorias = catsErro.Data;
            var centrosErro = await svc.ListarCentrosCustoAsync(ativo: true);
            if (centrosErro.Success && centrosErro.Data is not null) vmErro.CentrosCusto = centrosErro.Data;
            ViewBag.Title = "Nova Conta a Pagar";
            ViewBag.ActiveMenuItem = "ContasAPagar";
            return View("Criar", vmErro);
        }

        if (string.IsNullOrWhiteSpace(descricao))
            return await ReexibirComErro("Descricao e obrigatoria.");
        if (categoriaFinanceiraId == Guid.Empty)
            return await ReexibirComErro("Selecione ou crie uma categoria.");
        if (numeroParcelas < 1 || numeroParcelas > 36)
            return await ReexibirComErro("Numero de parcelas deve estar entre 1 e 36.");
        if (valorTotal <= 0m)
            return await ReexibirComErro("Valor total deve ser positivo.");
        if (primeiraVencimento.Date < dataEmissao.Date)
            return await ReexibirComErro("A 1a vencimento nao pode ser anterior a data de emissao.");

        var parcelas = MontarParcelas(valorTotal, numeroParcelas, primeiraVencimento, intervaloTipo);

        var result = await svc.CriarContaPagarAsync(
            descricao, categoriaFinanceiraId, dataEmissao, parcelas,
            fornecedorId, centroCustoId, observacoes, emitirAposCriar);

        if (HasError(result) || result.Data is null)
            return await ReexibirComErro(result.ErrorMessage ?? "Nao foi possivel criar a conta. Tente novamente.");

        Toast("success", "Conta a pagar criada com sucesso.");
        return RedirectToAction(nameof(Detalhe), new { id = result.Data.Id });
    }

    [HttpGet("/contas-a-pagar/{id:guid}")]
    public async Task<IActionResult> Detalhe(Guid id)
    {
        ViewBag.Title = "Detalhes da Conta a Pagar";
        ViewBag.ActiveMenuItem = "ContasAPagar";

        var result = await svc.ObterContaPagarAsync(id);
        if (HasError(result) || result.Data is null)
            return RedirectToAction(nameof(Index));

        return View(new ContaPagarDetalheViewModel { Conta = result.Data });
    }

    [HttpPost("/contas-a-pagar/{id:guid}/emitir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Emitir(Guid id)
    {
        var result = await svc.EmitirContaPagarAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Detalhe), new { id });
        Toast("success", "Conta emitida.");
        return RedirectToAction(nameof(Detalhe), new { id });
    }

    [HttpPost("/contas-a-pagar/{id:guid}/parcelas/{parcelaId:guid}/pagar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pagar(Guid id, Guid parcelaId, decimal valor, string metodo, string? observacao = null)
    {
        if (valor <= 0m) { Toast("error", "Valor deve ser positivo."); return RedirectToAction(nameof(Detalhe), new { id }); }
        if (string.IsNullOrWhiteSpace(metodo)) { Toast("error", "Metodo e obrigatorio."); return RedirectToAction(nameof(Detalhe), new { id }); }
        var r = await svc.RegistrarPagamentoCpAsync(id, parcelaId, valor, metodo, observacao);
        if (HasError(r)) return RedirectToAction(nameof(Detalhe), new { id });
        Toast("success", $"Pagamento de {valor:C} registrado.");
        return RedirectToAction(nameof(Detalhe), new { id });
    }

    [HttpPost("/contas-a-pagar/{id:guid}/parcelas/{parcelaId:guid}/pagamentos/{pagId:guid}/estornar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Estornar(Guid id, Guid parcelaId, Guid pagId, string? motivo = null)
    {
        var r = await svc.EstornarPagamentoCpAsync(parcelaId, pagId, motivo);
        if (HasError(r)) return RedirectToAction(nameof(Detalhe), new { id });
        Toast("success", "Pagamento estornado.");
        return RedirectToAction(nameof(Detalhe), new { id });
    }

    [HttpPost("/contas-a-pagar/{id:guid}/cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(Guid id, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            Toast("error", "Motivo e obrigatorio.");
            return RedirectToAction(nameof(Detalhe), new { id });
        }
        var result = await svc.CancelarContaPagarAsync(id, motivo);
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
