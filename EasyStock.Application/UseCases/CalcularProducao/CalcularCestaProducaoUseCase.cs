using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.CalcularProducao;

/// <summary>
/// Calcula consumo de insumos para uma cesta de produtos-finais (Onda 1, in-context a partir de Pedido).
/// Read-only: nao modifica estoque. Toleracia por item — RECIPE_NOT_FOUND vira Status.SemReceita;
/// outros erros viram Status.Erro com mensagem. Resto da cesta segue normalmente.
/// Otimizacao N+1: batch query 1) receitas dos N produtos, 2) saldos dos M insumos distintos.
/// </summary>
public class CalcularCestaProducaoUseCase(
    IProdutoComposicaoRepository composicaoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    ILogger<CalcularCestaProducaoUseCase> logger)
{
    public async Task<CalcularCestaProducaoResult> ExecuteAsync(CalcularCestaProducaoCommand command, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

        if (command.Itens.Count == 0)
            return new CalcularCestaProducaoResult(
                Itens: Array.Empty<ItemCestaResult>(),
                Consolidado: Array.Empty<InsumoConsolidadoResult>(),
                TudoDisponivel: true,
                CustoEstimadoTotal: null);

        foreach (var i in command.Itens)
        {
            UseCaseGuards.EnsureNotEmpty(i.ProdutoFinalId, "ProdutoFinalId");
            if (i.Quantidade < CalculoProducaoCore.PrecisaoMinima)
                throw new UseCaseValidationException("INVALID_QUANTITY", $"Quantidade minima e {CalculoProducaoCore.PrecisaoMinima}.");
        }

        // Batch 1: receitas dos N produtos-finais (Include ProdutoFinal + Insumo).
        var produtoIds = command.Itens.Select(i => i.ProdutoFinalId).Distinct().ToList();
        var composicoesPorProduto = await composicaoRepository.GetByProdutosFinaisAsync(
            command.EmpresaId, produtoIds, command.LojaId, ct);

        // Batch 2: saldos de TODOS os insumos distintos que aparecem nas receitas.
        var insumoIdsDistintos = composicoesPorProduto.Values
            .SelectMany(comps => comps.Select(c => c.InsumoId))
            .Distinct()
            .ToList();
        var saldosPorInsumo = insumoIdsDistintos.Count == 0
            ? new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>(0)
            : (Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>)await itemEstoqueRepository.GetByProdutosAsync(
                command.EmpresaId, insumoIdsDistintos, command.LojaId, ct);

        var itensResultado = new List<ItemCestaResult>(command.Itens.Count);
        var tudoDisponivel = true;
        var algumCustoConhecido = false;
        decimal custoTotal = 0m;

        foreach (var item in command.Itens)
        {
            if (!composicoesPorProduto.TryGetValue(item.ProdutoFinalId, out var composicoes) || composicoes.Count == 0)
            {
                // Tolerancia: produto sem receita nao trava cesta. UI mostra placeholder.
                itensResultado.Add(new ItemCestaResult(
                    ProdutoFinalId: item.ProdutoFinalId,
                    ProdutoNome: "(sem receita)",
                    Status: ItemCestaStatus.SemReceita,
                    Resultado: null,
                    Erro: null));
                continue;
            }

            var produto = composicoes.First().ProdutoFinal!;
            try
            {
                var resultado = CalculoProducaoCore.Calcular(
                    produto, item.Quantidade, item.Unidade, composicoes, saldosPorInsumo);

                if (!resultado.TudoDisponivel) tudoDisponivel = false;
                if (resultado.CustoEstimadoTotal.HasValue)
                {
                    custoTotal += resultado.CustoEstimadoTotal.Value;
                    algumCustoConhecido = true;
                }

                itensResultado.Add(new ItemCestaResult(
                    ProdutoFinalId: produto.Id,
                    ProdutoNome: produto.Nome,
                    Status: ItemCestaStatus.Ok,
                    Resultado: resultado,
                    Erro: null));
            }
            catch (UseCaseValidationException ex)
            {
                // Tolerancia: erro no item (rendimento invalido, unidade incompativel) nao derruba cesta.
                logger.LogWarning(ex, "Item da cesta com erro: produto {ProdutoId} -> {Code}", item.ProdutoFinalId, ex.Code);
                tudoDisponivel = false;
                itensResultado.Add(new ItemCestaResult(
                    ProdutoFinalId: produto.Id,
                    ProdutoNome: produto.Nome,
                    Status: ItemCestaStatus.Erro,
                    Resultado: null,
                    Erro: ex.Message));
            }
        }

        var consolidado = ConsolidarInsumos(itensResultado);
        if (consolidado.Any(c => c.Falta.HasValue || c.ConversaoFalhou))
            tudoDisponivel = false;

        logger.LogInformation(
            "Calculo de cesta: empresa {EmpresaId} {ItensCount} itens, {OkCount} ok, tudoDisponivel={Tudo}",
            command.EmpresaId, command.Itens.Count, itensResultado.Count(r => r.Status == ItemCestaStatus.Ok), tudoDisponivel);

        return new CalcularCestaProducaoResult(
            Itens: itensResultado,
            Consolidado: consolidado,
            TudoDisponivel: tudoDisponivel,
            CustoEstimadoTotal: algumCustoConhecido ? custoTotal : null);
    }

    /// <summary>
    /// Agrega insumos por InsumoId entre todos os itens calculados com Status.Ok. Quando 2
    /// produtos usam o mesmo insumo em unidades distintas mas conversiveis, soma na unidade
    /// do primeiro encontrado. Conversao incompativel marca a linha consolidada como falha.
    /// </summary>
    private static List<InsumoConsolidadoResult> ConsolidarInsumos(IReadOnlyList<ItemCestaResult> itens)
    {
        var porInsumo = new Dictionary<Guid, ConsolidadoBuilder>();

        foreach (var item in itens)
        {
            if (item.Status != ItemCestaStatus.Ok || item.Resultado == null) continue;

            foreach (var linha in item.Resultado.Insumos)
            {
                if (!porInsumo.TryGetValue(linha.InsumoId, out var builder))
                {
                    builder = new ConsolidadoBuilder
                    {
                        InsumoId = linha.InsumoId,
                        Nome = linha.InsumoNome,
                        UnidadeReceita = linha.UnidadeReceita,
                        Saldo = linha.SaldoAtual,
                        UnidadeSaldo = linha.UnidadeSaldo,
                        CustoUnitario = linha.CustoUnitarioReferencia
                    };
                    porInsumo[linha.InsumoId] = builder;
                }

                if (builder.ConversaoFalhou) continue;

                // Soma quantidade na unidade-receita do builder (primeiro encontrado).
                if (linha.UnidadeReceita == builder.UnidadeReceita)
                {
                    builder.Precisa += linha.QuantidadeNecessaria;
                }
                else
                {
                    var (qtdConvertida, erro) = UnidadeMedidaConverter.Converter(
                        linha.QuantidadeNecessaria, linha.UnidadeReceita, builder.UnidadeReceita);
                    if (qtdConvertida.HasValue)
                    {
                        builder.Precisa += qtdConvertida.Value;
                    }
                    else
                    {
                        builder.ConversaoFalhou = true;
                        builder.Aviso = $"Conflito de unidade entre produtos: {erro}";
                    }
                }
            }
        }

        var result = new List<InsumoConsolidadoResult>(porInsumo.Count);
        foreach (var b in porInsumo.Values.OrderBy(b => b.Nome))
        {
            decimal? falta = null;
            bool conversaoFalhou = b.ConversaoFalhou;

            if (!conversaoFalhou)
            {
                // Converte saldo (unidade base do insumo) pra unidade do builder
                var (saldoConvertido, erroConversao) = UnidadeMedidaConverter.Converter(
                    b.Saldo, b.UnidadeSaldo, b.UnidadeReceita);

                if (saldoConvertido == null)
                {
                    conversaoFalhou = true;
                    b.Aviso = $"Saldo em unidade incompativel: {erroConversao}";
                    falta = b.Precisa;
                }
                else if (saldoConvertido.Value < b.Precisa)
                {
                    falta = b.Precisa - saldoConvertido.Value;
                }
            }

            decimal? custoEstimado = b.CustoUnitario.HasValue ? b.CustoUnitario.Value * b.Precisa : null;

            result.Add(new InsumoConsolidadoResult(
                InsumoId: b.InsumoId,
                InsumoNome: b.Nome,
                Precisa: b.Precisa,
                UnidadeReceita: b.UnidadeReceita,
                Saldo: b.Saldo,
                UnidadeSaldo: b.UnidadeSaldo,
                Falta: falta,
                CustoEstimado: custoEstimado,
                ConversaoFalhou: conversaoFalhou,
                Aviso: b.Aviso));
        }
        return result;
    }

    private sealed class ConsolidadoBuilder
    {
        public Guid InsumoId;
        public string Nome = string.Empty;
        public UnidadeMedida UnidadeReceita;
        public decimal Precisa;
        public decimal Saldo;
        public UnidadeMedida UnidadeSaldo;
        public decimal? CustoUnitario;
        public bool ConversaoFalhou;
        public string? Aviso;
    }
}
