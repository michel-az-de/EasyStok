using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.GerenciarProduto;

/// <summary>
/// #3299: o desempenho de cada produto e a faixa da tela Produtos de Cadastros. As janelas começam à meia-noite de
/// Brasília: "90 dias" é hoje e os 89 anteriores; a anterior é o mesmo tamanho logo antes; a curva e o preço vendido
/// usam 365 dias. A faixa é a soma das linhas, para a tela nunca mostrar dois números para a mesma coisa.
/// </summary>
public sealed class DesempenhoDosProdutosUseCase(IProdutosDesempenhoQuery consulta, TimeProvider relogio)
{
    public const int MaisVendidos = 3;

    public async Task<DesempenhoDosProdutosResult> ExecuteAsync(Guid empresaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var hoje = HorarioBrasil.DataOperacional(relogio.GetUtcNow().UtcDateTime);
        var janela = DesempenhoDoProduto.JanelaDias;
        var inicioJanela = HorarioBrasil.InicioRealDoDiaUtc(hoje.AddDays(-(janela - 1)));
        var inicioAnterior = HorarioBrasil.InicioRealDoDiaUtc(hoje.AddDays(-(2 * janela - 1)));
        var inicioAno = HorarioBrasil.InicioRealDoDiaUtc(hoje.AddDays(-(DesempenhoDoProduto.JanelaCurvaDias - 1)));

        // Um DbContext por requisição: leituras em sequência.
        var catalogo = await consulta.CatalogoAsync(empresaId, ct);
        var vendas = await consulta.VendasDesdeAsync(empresaId, inicioAno, ct);
        var historico = (await consulta.HistoricoAsync(empresaId, ct)).ToDictionary(h => h.ProdutoId);
        var receitaSemProduto = await consulta.ReceitaSemProdutoDesdeAsync(empresaId, inicioJanela, ct);

        var naJanela = vendas.Where(v => v.VendidoEm >= inicioJanela).ToList();
        var naAnterior = vendas.Where(v => v.VendidoEm >= inicioAnterior && v.VendidoEm < inicioJanela).ToList();
        var curva = DesempenhoDoProduto.CurvaAbc(vendas.GroupBy(v => v.ProdutoId).Select(g => (g.Key, g.Sum(v => v.Receita))));
        var receitaDaJanela = naJanela.Sum(v => v.Receita);

        var linhas = catalogo.Select(p => Linha(p, hoje, naJanela, naAnterior, vendas, historico.GetValueOrDefault(p.Id), curva, receitaDaJanela)).ToList();
        var ativos = catalogo.Where(p => p.Ativo && p.DeVenda).Select(p => p.Id).ToHashSet();
        var anoA = linhas.Where(l => l.Curva == DesempenhoDoProduto.CurvaA).Select(l => l.ProdutoId).ToHashSet();
        var receitaDoAno = vendas.Sum(v => v.Receita);
        var comprasDoAno = vendas.Select(v => v.PedidoId).Distinct().Count();
        var nomes = catalogo.ToDictionary(p => p.Id, p => p.Nome);

        var resumo = new ResumoDosProdutos(
            Receita: receitaDaJanela,
            ReceitaAnterior: naAnterior.Sum(v => v.Receita),
            Compras: naJanela.Select(v => v.PedidoId).Distinct().Count(),
            ComprasAnterior: naAnterior.Select(v => v.PedidoId).Distinct().Count(),
            ProdutosVendidos: naJanela.Select(v => v.ProdutoId).Where(ativos.Contains).Distinct().Count(),
            ReceitaSemProduto: receitaSemProduto,
            Ativos: ativos.Count,
            Parados: linhas.Count(l => l.Parado),
            NuncaVenderam: linhas.Count(l => l.NuncaVendeu),
            AbaixoDaTabela: linhas.Count(l => l.AbaixoDaTabela && ativos.Contains(l.ProdutoId)),
            SemCusto: catalogo.Count(p => ativos.Contains(p.Id) && !(p.Custo > 0m)),
            SemPreco: catalogo.Count(p => ativos.Contains(p.Id) && !(p.PrecoTabela > 0m)),
            Curva: new CurvaDosProdutos(
                ProdutosA: anoA.Count,
                FatiaA: receitaDoAno == 0m ? 0m : Math.Round(vendas.Where(v => anoA.Contains(v.ProdutoId)).Sum(v => v.Receita) / receitaDoAno * 100m, 1, MidpointRounding.AwayFromZero),
                ComprasNoAno: comprasDoAno,
                Previa: comprasDoAno < DesempenhoDoProduto.ComprasParaCurvaFirme),
            MaisVendidos: linhas.Where(l => l.Receita > 0m).OrderByDescending(l => l.Receita).ThenBy(l => nomes[l.ProdutoId])
                .Take(MaisVendidos).Select(l => new ProdutoMaisVendido(l.ProdutoId, nomes[l.ProdutoId], l.Receita)).ToList());

        return new DesempenhoDosProdutosResult(janela, hoje, resumo, linhas);
    }

    private static DesempenhoDeUmProduto Linha(
        ProdutoDoCatalogo p, DateOnly hoje, List<VendaDoProdutoNoPedido> naJanela, List<VendaDoProdutoNoPedido> naAnterior,
        IReadOnlyList<VendaDoProdutoNoPedido> doAno, HistoricoDoProduto? h, IReadOnlyDictionary<Guid, string> curva, decimal receitaDaJanela)
    {
        var atual = naJanela.Where(v => v.ProdutoId == p.Id).ToList();
        var antes = naAnterior.Where(v => v.ProdutoId == p.Id).ToList();
        var ano = doAno.Where(v => v.ProdutoId == p.Id).ToList();
        var receita = atual.Sum(v => v.Receita);
        var unidades = atual.Sum(v => v.Unidades);
        var pedidos = atual.Select(v => v.PedidoId).Distinct().Count();
        var unidadesAntes = antes.Sum(v => v.Unidades);
        var pedidosAntes = antes.Select(v => v.PedidoId).Distinct().Count();

        DateOnly? primeira = h is null ? null : HorarioBrasil.DataOperacional(h.PrimeiraVendaEm);
        DateOnly? ultima = h is null ? null : HorarioBrasil.DataOperacional(h.UltimaVendaEm);
        var precoVendido = DesempenhoDoProduto.PrecoVendido(ano.Sum(v => v.Receita), ano.Sum(v => v.Unidades));
        var ativoDeVenda = p.Ativo && p.DeVenda;

        return new DesempenhoDeUmProduto(
            ProdutoId: p.Id,
            Receita: receita,
            Unidades: unidades,
            Pedidos: pedidos,
            ReceitaAnterior: antes.Sum(v => v.Receita),
            FatiaDaReceita: receitaDaJanela == 0m ? 0m : Math.Round(receita / receitaDaJanela * 100m, 1, MidpointRounding.AwayFromZero),
            Tendencia: DesempenhoDoProduto.Tendencia(pedidos, unidades, pedidosAntes, unidadesAntes),
            RitmoDe30Dias: DesempenhoDoProduto.RitmoDe30Dias(pedidos, unidades),
            Curva: curva.GetValueOrDefault(p.Id),
            UltimaVenda: ultima,
            DiasSemVender: ultima is { } u ? hoje.DayNumber - u.DayNumber : null,
            PedidosNoHistorico: h?.Pedidos ?? 0,
            Parado: ativoDeVenda && DesempenhoDoProduto.Parado(h?.Pedidos ?? 0, primeira, ultima, hoje),
            NuncaVendeu: DesempenhoDoProduto.NuncaVendeu(p.Ativo, HorarioBrasil.DataOperacional(p.CriadoEm), h is not null, p.DeVenda, hoje),
            PrecoVendido: precoVendido,
            PrecoTabela: p.PrecoTabela > 0m ? p.PrecoTabela : null,
            Custo: p.Custo > 0m ? p.Custo : null,
            Margem: DesempenhoDoProduto.Margem(precoVendido ?? p.PrecoTabela ?? 0m, p.Custo),
            AbaixoDaTabela: ativoDeVenda && precoVendido is { } pv && DesempenhoDoProduto.AbaixoDaTabela(pv, p.PrecoTabela));
    }
}

public sealed record DesempenhoDosProdutosResult(int JanelaDias, DateOnly Hoje, ResumoDosProdutos Resumo, IReadOnlyList<DesempenhoDeUmProduto> Produtos);

public sealed record ResumoDosProdutos(
    decimal Receita,
    decimal ReceitaAnterior,
    int Compras,
    int ComprasAnterior,
    int ProdutosVendidos,
    decimal ReceitaSemProduto,
    int Ativos,
    int Parados,
    int NuncaVenderam,
    int AbaixoDaTabela,
    int SemCusto,
    int SemPreco,
    CurvaDosProdutos Curva,
    IReadOnlyList<ProdutoMaisVendido> MaisVendidos);

/// <summary>Curva de 365 dias: quantos produtos são A e quanto da receita do ano eles fazem. Prévia com pouca base.</summary>
public sealed record CurvaDosProdutos(int ProdutosA, decimal FatiaA, int ComprasNoAno, bool Previa);

public sealed record ProdutoMaisVendido(Guid ProdutoId, string Nome, decimal Receita);

/// <summary>Receita, unidades e pedidos são dos últimos 90 dias; preço vendido e curva, de 365. Sem dado, nulo.</summary>
public sealed record DesempenhoDeUmProduto(
    Guid ProdutoId,
    decimal Receita,
    decimal Unidades,
    int Pedidos,
    decimal ReceitaAnterior,
    decimal FatiaDaReceita,
    string? Tendencia,
    decimal? RitmoDe30Dias,
    string? Curva,
    DateOnly? UltimaVenda,
    int? DiasSemVender,
    int PedidosNoHistorico,
    bool Parado,
    bool NuncaVendeu,
    decimal? PrecoVendido,
    decimal? PrecoTabela,
    decimal? Custo,
    decimal? Margem,
    bool AbaixoDaTabela);
