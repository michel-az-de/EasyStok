using System.Diagnostics.Metrics;

namespace EasyStock.Api.Observability;

public class MetricsService
{
    private readonly Counter<long> _entradasEstoqueCounter;
    private readonly Counter<long> _saidasEstoqueCounter;
    private readonly Counter<long> _reposicoesEstoqueCounter;
    private readonly Counter<long> _vendasCounter;
    private readonly Counter<long> _falhasOperacaoCounter;

    public MetricsService(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("EasyStock.Api");
        _entradasEstoqueCounter = meter.CreateCounter<long>("entradas_estoque_total", description: "Total de entradas de estoque registradas");
        _saidasEstoqueCounter = meter.CreateCounter<long>("saidas_estoque_total", description: "Total de saídas de estoque registradas");
        _reposicoesEstoqueCounter = meter.CreateCounter<long>("reposicoes_estoque_total", description: "Total de reposições de estoque registradas");
        _vendasCounter = meter.CreateCounter<long>("vendas_total", description: "Total de vendas registradas");
        _falhasOperacaoCounter = meter.CreateCounter<long>("falhas_operacao_total", description: "Total de falhas em operações");
    }

    public void IncrementEntradasEstoque() => _entradasEstoqueCounter.Add(1);
    public void IncrementSaidasEstoque() => _saidasEstoqueCounter.Add(1);
    public void IncrementReposicoesEstoque() => _reposicoesEstoqueCounter.Add(1);
    public void IncrementVendas() => _vendasCounter.Add(1);
    public void IncrementFalhasOperacao(string operacao) => _falhasOperacaoCounter.Add(1, new KeyValuePair<string, object?>("operacao", operacao));
}