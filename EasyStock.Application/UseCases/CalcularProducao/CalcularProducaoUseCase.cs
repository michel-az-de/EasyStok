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
    public async Task<CalcularProducaoResult> ExecuteAsync(CalcularProducaoCommand command, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(command.ProdutoFinalId, "ProdutoFinalId");
        if (command.QuantidadeDesejada < CalculoProducaoCore.PrecisaoMinima)
            throw new UseCaseValidationException("INVALID_QUANTITY", $"Quantidade minima e {CalculoProducaoCore.PrecisaoMinima}.");

        var produto = await produtoRepository.GetByIdAsync(command.EmpresaId, command.ProdutoFinalId)
            ?? throw new UseCaseValidationException("RECIPE_NOT_FOUND", "Produto-final nao encontrado.");

        // Defesa em profundidade — Global Query Filter ja cobre, mas validamos de novo.
        if (produto.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("CROSS_TENANT", "Produto pertence a outra empresa.");

        var composicoes = await composicaoRepository.GetByProdutoFinalAsync(command.EmpresaId, command.ProdutoFinalId, command.LojaId, ct);
        if (composicoes.Count == 0)
            throw new UseCaseValidationException("RECIPE_NOT_FOUND", "Produto nao tem receita cadastrada.");

        // Batch query: 1 round trip pra trazer estoque de TODOS os insumos da receita
        var insumoIds = composicoes.Select(c => c.InsumoId).Distinct().ToList();
        var saldosPorInsumo = await itemEstoqueRepository.GetByProdutosAsync(command.EmpresaId, insumoIds, command.LojaId, ct);

        var resultado = CalculoProducaoCore.Calcular(
            produto, command.QuantidadeDesejada, command.UnidadeDesejada, composicoes, saldosPorInsumo);

        logger.LogInformation(
            "Calculo de producao: produto {ProdutoId} empresa {EmpresaId} qtd {Qtd} {Unidade} -> fator {Fator}, {InsumoCount} insumos, tudoDisponivel={Tudo}",
            command.ProdutoFinalId, command.EmpresaId, command.QuantidadeDesejada, command.UnidadeDesejada,
            resultado.FatorMultiplicador, resultado.Insumos.Count, resultado.TudoDisponivel);

        return resultado;
    }
}
