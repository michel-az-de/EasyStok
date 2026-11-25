namespace EasyStock.Domain.Sales;

/// <summary>
/// #3299: as regras do desempenho do produto na tela Produtos de Cadastros. Venda é <see cref="RegraDeCompra"/>;
/// as contas aqui são puras e exigem base mínima, porque a loja vende pouco e número sem base engana.
/// </summary>
public static class DesempenhoDoProduto
{
    public const string CurvaA = "A";
    public const string CurvaB = "B";
    public const string CurvaC = "C";

    public const string Subindo = "subindo";
    public const string Caindo = "caindo";
    public const string Estavel = "estavel";

    /// <summary>Janela das vendas e da tendência (e a anterior, de mesmo tamanho).</summary>
    public const int JanelaDias = 90;
    /// <summary>Janela da curva ABC e do preço vendido: um ano, a base mais estável que a loja tem.</summary>
    public const int JanelaCurvaDias = 365;
    public const int MinimoDePedidos = 3;
    public const decimal VariacaoDaTendencia = 0.25m;
    public const decimal UnidadesDaTendencia = 2m;
    public const int DiasParaParado = 60;
    public const int DiasParaNuncaVendeu = 30;
    public const decimal FolgaDaTabela = 0.98m;
    /// <summary>Com menos compras que isto na janela da curva, a tela chama a curva de prévia.</summary>
    public const int ComprasParaCurvaFirme = 100;

    private const decimal LimiteA = 0.80m;
    private const decimal LimiteB = 0.95m;

    /// <summary>
    /// Curva ABC pela receita acumulada, da maior para a menor: até 80% é A, até 95% é B, o resto é C. O primeiro é
    /// sempre A, mesmo quando sozinho passa de 80%. Produto sem receita fica fora (não tem curva).
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> CurvaAbc(IEnumerable<(Guid ProdutoId, decimal Receita)> receitas)
    {
        var ordenadas = receitas.Where(r => r.Receita > 0m).OrderByDescending(r => r.Receita).ThenBy(r => r.ProdutoId).ToList();
        var total = ordenadas.Sum(r => r.Receita);
        var curva = new Dictionary<Guid, string>(ordenadas.Count);
        var acumulado = 0m;
        foreach (var (id, receita) in ordenadas)
        {
            acumulado += receita;
            var fatia = acumulado / total;
            curva[id] = curva.Count == 0 || fatia <= LimiteA ? CurvaA : fatia <= LimiteB ? CurvaB : CurvaC;
        }
        return curva;
    }

    /// <summary>
    /// Tendência das unidades contra a janela anterior. Só com <see cref="MinimoDePedidos"/> pedidos nas duas janelas;
    /// sobe ou cai com 25% e 2 unidades de diferença, senão é estável. Sem base, nulo.
    /// </summary>
    public static string? Tendencia(int pedidos, decimal unidades, int pedidosAntes, decimal unidadesAntes)
    {
        if (pedidos < MinimoDePedidos || pedidosAntes < MinimoDePedidos || unidadesAntes <= 0m) return null;
        var diferenca = unidades - unidadesAntes;
        if (Math.Abs(diferenca) < UnidadesDaTendencia || Math.Abs(diferenca) / unidadesAntes < VariacaoDaTendencia) return Estavel;
        return diferenca > 0m ? Subindo : Caindo;
    }

    /// <summary>Previsão simples para os próximos 30 dias: um terço das unidades dos últimos 90, com base mínima.</summary>
    public static decimal? RitmoDe30Dias(int pedidos, decimal unidades) =>
        pedidos < MinimoDePedidos ? null : Math.Round(unidades * 30m / JanelaDias, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Parado: vendeu em <see cref="MinimoDePedidos"/> pedidos ou mais e está sem vender há mais de 60 dias e há mais
    /// de duas vezes o próprio intervalo médio entre pedidos. Produto de venda esporádica não acende alerta.
    /// </summary>
    public static bool Parado(int pedidosNoHistorico, DateOnly? primeiraVenda, DateOnly? ultimaVenda, DateOnly hoje)
    {
        if (pedidosNoHistorico < MinimoDePedidos || primeiraVenda is not { } primeira || ultimaVenda is not { } ultima) return false;
        var semVender = hoje.DayNumber - ultima.DayNumber;
        var intervaloMedio = (decimal)(ultima.DayNumber - primeira.DayNumber) / (pedidosNoHistorico - 1);
        return semVender > DiasParaParado && semVender > 2m * intervaloMedio;
    }

    /// <summary>Produto de venda (não insumo nem embalagem), ativo, cadastrado há 30 dias ou mais e sem nenhuma venda.</summary>
    public static bool NuncaVendeu(bool ativo, DateOnly criadoEm, bool temVenda, bool deVenda, DateOnly hoje) =>
        ativo && deVenda && !temVenda && hoje.DayNumber - criadoEm.DayNumber >= DiasParaNuncaVendeu;

    /// <summary>Preço médio praticado: receita sobre unidades, 2 casas. Variações dividem o produto e não têm preço próprio.</summary>
    public static decimal? PrecoVendido(decimal receita, decimal unidades) =>
        receita <= 0m || unidades <= 0m ? null : Math.Round(receita / unidades, 2, MidpointRounding.AwayFromZero);

    /// <summary>Margem sobre o preço, em %, 1 casa. Sem custo informado (nulo ou zero) ou sem preço, não há margem.</summary>
    public static decimal? Margem(decimal preco, decimal? custo) =>
        preco <= 0m || custo is not { } c || c <= 0m ? null : Math.Round((preco - c) / preco * 100m, 1, MidpointRounding.AwayFromZero);

    /// <summary>Vendido abaixo do preço de tabela, com 2% de folga para arredondamento e troco.</summary>
    public static bool AbaixoDaTabela(decimal precoVendido, decimal? precoTabela) =>
        precoTabela is { } t && t > 0m && precoVendido < t * FolgaDaTabela;
}
