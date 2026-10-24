using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Services;

/// <summary>Como escolher a ordem dos lotes numa saída.</summary>
/// <param name="Fefo">
/// true (padrão): o lote que vence antes sai antes; lote sem validade por último. false: FIFO puro, só
/// pela data de entrada (<c>ConfiguracaoLoja.FifoAtivo = false</c>).
/// </param>
/// <param name="VencidoPorUltimo">
/// true (padrão): lote vencido só sai depois dos que ainda valem. false: o vencido sai primeiro, que é o
/// que se quer ao dar baixa de perda, descarte ou vencimento (<c>PermiteBaixaDeLoteVencido</c>).
/// </param>
public sealed record RegraDeConsumo(bool Fefo = true, bool VencidoPorUltimo = true);

/// <summary>Quanto sai de um lote. <paramref name="Descoberto"/> é a parte da saída que o lote não tinha (a falta).</summary>
public sealed record ConsumoDeLote(ItemEstoque Lote, decimal Quantidade, decimal Descoberto);

/// <summary>O que uma saída vai tirar de cada lote, antes de tirar.</summary>
public sealed class PlanoDeConsumo
{
    public PlanoDeConsumo(IReadOnlyList<ConsumoDeLote> consumos, decimal falta)
    {
        Consumos = consumos;
        Falta = falta;
    }

    /// <summary>Uma fatia por lote que baixa, na ordem em que baixa. A falta, se houver, está na última.</summary>
    public IReadOnlyList<ConsumoDeLote> Consumos { get; }

    /// <summary>Quanto os lotes não cobrem. Zero quando o saldo basta.</summary>
    public decimal Falta { get; }

    /// <summary>
    /// Baixa os lotes. A parte da falta vira <see cref="ItemEstoque.QuantidadeDescoberta"/> no lote que a
    /// recebeu; quem não aceita descoberto confere <see cref="Falta"/> antes de chamar.
    /// </summary>
    public void Aplicar(DateTime dataSaida, DateTime alteradoEm, bool permitirVencido)
    {
        foreach (var consumo in Consumos)
        {
            var quantidade = Quantidade.From(consumo.Quantidade);
            if (consumo.Descoberto > 0m)
                consumo.Lote.RegistrarSaidaPermitindoDescoberto(quantidade, dataSaida, alteradoEm, permitirVencido);
            else
                consumo.Lote.RegistrarSaida(quantidade, dataSaida, alteradoEm, permitirVencido);
        }
    }
}

/// <summary>
/// A regra única de consumo de lotes (#2003, estoque-17): a venda, a esteira do pedido e o PWA escolhiam
/// os lotes cada um do seu jeito. Aqui fica uma só, pura (sem repositório):
/// <list type="number">
/// <item>só entram lotes com saldo; lote zerado não baixa (estoque-06);</item>
/// <item>lote vencido por último (ou primeiro, se <see cref="RegraDeConsumo.VencidoPorUltimo"/> for false);</item>
/// <item>FEFO: validade crescente, sem validade por último; FIFO puro se <see cref="RegraDeConsumo.Fefo"/> for false;</item>
/// <item>empate: o que entrou antes, e depois o criado antes (FIFO);</item>
/// <item>a falta vira descoberto no último lote que baixou; se nenhum baixou, no primeiro da ordem.</item>
/// </list>
/// </summary>
public static class ConsumoDeLotes
{
    public static IReadOnlyList<ItemEstoque> Ordenar(IEnumerable<ItemEstoque> lotes, DateTime referencia, RegraDeConsumo regra) =>
        lotes
            .OrderBy(l => regra.VencidoPorUltimo && l.VenceuEm(referencia) ? 1 : 0)
            .ThenBy(l => regra.Fefo ? l.ValidadeEm?.DataValidade ?? DateTime.MaxValue : DateTime.MinValue)
            .ThenBy(l => l.EntradaEm)
            .ThenBy(l => l.CriadoEm)
            .ToList();

    /// <param name="lotes">Os lotes elegíveis do produto (já filtrados por loja, porção e status).</param>
    /// <param name="quantidade">Quanto sai.</param>
    /// <param name="referencia">O instante da saída, que decide o que está vencido.</param>
    public static PlanoDeConsumo Planejar(IEnumerable<ItemEstoque> lotes, decimal quantidade, DateTime referencia, RegraDeConsumo regra)
    {
        var ordenados = Ordenar(lotes, referencia, regra);
        var consumos = new List<(ItemEstoque Lote, decimal Quantidade)>();
        var restante = quantidade;

        foreach (var lote in ordenados)
        {
            if (restante <= 0m) break;
            var saldo = lote.QuantidadeAtual?.Value ?? 0m;
            if (saldo <= 0m) continue;

            var consumir = Math.Min(restante, saldo);
            consumos.Add((lote, consumir));
            restante -= consumir;
        }

        var fatias = consumos.Select(c => new ConsumoDeLote(c.Lote, c.Quantidade, 0m)).ToList();
        if (restante <= 0m) return new PlanoDeConsumo(fatias, 0m);

        if (fatias.Count > 0)
            fatias[^1] = fatias[^1] with { Quantidade = fatias[^1].Quantidade + restante, Descoberto = restante };
        else if (ordenados.Count > 0)
            fatias.Add(new ConsumoDeLote(ordenados[0], restante, restante));
        return new PlanoDeConsumo(fatias, restante);
    }
}
