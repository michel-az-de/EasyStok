using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.CalcularProducao;

/// <summary>
/// Simula consumo de insumos para producao de uma quantidade-alvo do produto-final.
/// Read-only: nao modifica estoque. Compara saldo atual com necessidade e marca falta por linha.
/// Multi-loja: <see cref="CalcularProducaoCommand.LojaId"/> define receita-override + filtro de estoque.
/// </summary>
public class CalcularProducaoUseCase(
    IProdutoRepository produtoRepository,
    IProdutoComposicaoRepository composicaoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    ILogger<CalcularProducaoUseCase> logger)
{
    private const decimal PrecisaoMinima = 0.0001m;

    public async Task<CalcularProducaoResult> ExecuteAsync(CalcularProducaoCommand command, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(command.ProdutoFinalId, "ProdutoFinalId");
        if (command.QuantidadeDesejada < PrecisaoMinima)
            throw new UseCaseValidationException("INVALID_QUANTITY", $"Quantidade minima e {PrecisaoMinima}.");

        var produto = await produtoRepository.GetByIdAsync(command.EmpresaId, command.ProdutoFinalId)
            ?? throw new UseCaseValidationException("RECIPE_NOT_FOUND", "Produto-final nao encontrado.");

        // Defesa em profundidade — Global Query Filter ja cobre, mas validamos de novo.
        if (produto.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("CROSS_TENANT", "Produto pertence a outra empresa.");

        var composicoes = await composicaoRepository.GetByProdutoFinalAsync(command.EmpresaId, command.ProdutoFinalId, command.LojaId, ct);
        if (composicoes.Count == 0)
            throw new UseCaseValidationException("RECIPE_NOT_FOUND", "Produto nao tem receita cadastrada.");

        // Converte qtd desejada pra unidade do rendimento do produto antes de calcular o fator.
        var (qtdNoRendimento, erroConversaoRendimento) = UnidadeMedidaConverter.Converter(
            command.QuantidadeDesejada, command.UnidadeDesejada, produto.RendimentoUnidade);

        if (qtdNoRendimento == null)
            throw new UseCaseValidationException(
                "UNIT_INCOMPATIBLE",
                $"Unidade desejada ({command.UnidadeDesejada}) incompativel com rendimento do produto ({produto.RendimentoUnidade}): {erroConversaoRendimento}");

        if (produto.RendimentoBase <= 0)
            throw new UseCaseValidationException("INVALID_RENDIMENTO", "Rendimento do produto deve ser maior que zero.");

        var fator = qtdNoRendimento.Value / produto.RendimentoBase;

        // Batch query: 1 round trip pra trazer estoque de TODOS os insumos da receita
        var insumoIds = composicoes.Select(c => c.InsumoId).Distinct().ToList();
        var saldosPorInsumo = await itemEstoqueRepository.GetByProdutosAsync(command.EmpresaId, insumoIds, command.LojaId, ct);

        var linhasResultado = new List<CalcularInsumoResult>(composicoes.Count);
        decimal custoTotal = 0m;
        var algumCustoConhecido = false;
        var tudoDisponivel = true;

        foreach (var comp in composicoes)
        {
            var qtdNecessaria = comp.Quantidade * fator;
            var insumo = comp.Insumo!;
            var nomeInsumo = insumo.Nome;

            // Soma saldo de todos os lotes do insumo na loja informada (ou cross-loja se LojaId null)
            decimal saldoNaUnidadeBase = 0m;
            if (saldosPorInsumo.TryGetValue(comp.InsumoId, out var lotes))
                saldoNaUnidadeBase = lotes.Sum(l => l.QuantidadeAtual.Value);

            // Converte saldo (na unidade base do insumo) pra unidade da receita
            var (saldoConvertido, erroConversao) = UnidadeMedidaConverter.Converter(
                saldoNaUnidadeBase, insumo.UnidadeMedidaBase, comp.Unidade);

            bool conversaoFalhou = saldoConvertido == null;
            decimal saldoFinal = saldoConvertido ?? 0m;
            decimal? falta = null;

            if (conversaoFalhou)
            {
                falta = qtdNecessaria;
                tudoDisponivel = false;
            }
            else if (saldoFinal < qtdNecessaria)
            {
                falta = qtdNecessaria - saldoFinal;
                tudoDisponivel = false;
            }

            // Custo: ItemEstoque.CustoUnitario mais recente -> Produto.CustoReferencia -> null
            decimal? custoUnit = null;
            if (lotes is { Count: > 0 })
            {
                // GetByProdutosAsync ordena por EntradaEm desc — primeiro lote = mais recente
                custoUnit = lotes.First().CustoUnitario.Valor;
            }
            else if (insumo.CustoReferencia != null)
            {
                custoUnit = insumo.CustoReferencia.Valor;
            }

            decimal? custoLinha = custoUnit.HasValue ? custoUnit.Value * qtdNecessaria : null;
            if (custoLinha.HasValue)
            {
                custoTotal += custoLinha.Value;
                algumCustoConhecido = true;
            }

            linhasResultado.Add(new CalcularInsumoResult(
                InsumoId: comp.InsumoId,
                InsumoNome: nomeInsumo,
                QuantidadeNecessaria: qtdNecessaria,
                UnidadeReceita: comp.Unidade,
                SaldoAtual: saldoNaUnidadeBase,
                UnidadeSaldo: insumo.UnidadeMedidaBase,
                Falta: falta,
                CustoUnitarioReferencia: custoUnit,
                CustoEstimadoLinha: custoLinha,
                ConversaoFalhou: conversaoFalhou,
                Aviso: conversaoFalhou ? erroConversao : null));
        }

        logger.LogInformation(
            "Calculo de producao: produto {ProdutoId} empresa {EmpresaId} qtd {Qtd} {Unidade} -> fator {Fator}, {InsumoCount} insumos, tudoDisponivel={Tudo}",
            command.ProdutoFinalId, command.EmpresaId, command.QuantidadeDesejada, command.UnidadeDesejada, fator, linhasResultado.Count, tudoDisponivel);

        return new CalcularProducaoResult(
            ProdutoFinalId: produto.Id,
            ProdutoFinalNome: produto.Nome,
            QuantidadeDesejada: command.QuantidadeDesejada,
            UnidadeDesejada: command.UnidadeDesejada,
            FatorMultiplicador: fator,
            Insumos: linhasResultado,
            TudoDisponivel: tudoDisponivel,
            CustoEstimadoTotal: algumCustoConhecido ? custoTotal : null);
    }
}
