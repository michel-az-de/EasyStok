namespace EasyStock.Application.UseCases.PreviewSugestaoCompra;

/// <summary>
/// Read-only: dado um conjunto de insumos faltantes (output da calculadora), agrupa por
/// fornecedor preferido (mais frequente em ItemEstoque.FornecedorId nos lotes recentes) e
/// monta payload para o operador revisar. NAO cria PedidoFornecedor — isso e o
/// <see cref="CriarSugestaoCompra.CriarSugestaoCompraUseCase"/>.
/// </summary>
public class PreviewSugestaoCompraUseCase(
    IProdutoRepository produtoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    IFornecedorRepository fornecedorRepository,
    ILogger<PreviewSugestaoCompraUseCase> logger)
{
    public async Task<PreviewSugestaoCompraResult> ExecuteAsync(
        PreviewSugestaoCompraCommand command, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

        if (command.Insumos.Count == 0)
            return new PreviewSugestaoCompraResult([], null);

        // Batch query saldo: pega lotes recentes pra extrair fornecedor preferido + custo de referencia.
        var insumoIds = command.Insumos.Select(i => i.InsumoId).Distinct().ToList();
        var saldosPorInsumo = await itemEstoqueRepository.GetByProdutosAsync(
            command.EmpresaId, insumoIds, command.LojaId, ct);

        var linhasComFornecedor = new List<(Guid? FornecedorId, SugestaoLinhaResult Linha)>(command.Insumos.Count);
        decimal totalGeral = 0m;
        var algumCustoConhecido = false;

        foreach (var insumoInput in command.Insumos)
        {
            var produto = await produtoRepository.GetByIdAsync(command.EmpresaId, insumoInput.InsumoId);
            if (produto == null)
                throw new UseCaseValidationException(
                    "CROSS_TENANT_INSUMO",
                    $"Insumo {insumoInput.InsumoId} nao pertence a esta empresa.");

            saldosPorInsumo.TryGetValue(insumoInput.InsumoId, out var lotes);

            // Fornecedor preferido: mais frequente nos lotes recentes
            Guid? fornecedorPreferidoId = null;
            if (lotes is { Count: > 0 })
            {
                fornecedorPreferidoId = lotes
                    .Take(5)
                    .Where(l => l.FornecedorId.HasValue)
                    .GroupBy(l => l.FornecedorId!.Value)
                    .OrderByDescending(g => g.Count())
                    .Select(g => (Guid?)g.Key)
                    .FirstOrDefault();
            }

            // Custo chain: ItemEstoque.CustoUnitario (mais recente) -> Produto.CustoReferencia
            decimal? custo = null;
            if (lotes is { Count: > 0 })
                custo = lotes.First().CustoUnitario.Valor;
            else if (produto.CustoReferencia != null)
                custo = produto.CustoReferencia.Valor;

            decimal? subtotal = custo.HasValue ? custo.Value * insumoInput.QuantidadeFaltante : null;
            if (subtotal.HasValue)
            {
                totalGeral += subtotal.Value;
                algumCustoConhecido = true;
            }

            linhasComFornecedor.Add((fornecedorPreferidoId, new SugestaoLinhaResult(
                InsumoId: produto.Id,
                InsumoNome: produto.Nome,
                Quantidade: insumoInput.QuantidadeFaltante,
                Unidade: insumoInput.Unidade,
                CustoUnitarioReferencia: custo,
                Subtotal: subtotal)));
        }

        // Agrupa: fornecedores conhecidos + bucket null no final
        var grupos = new List<SugestaoPorFornecedorResult>();

        var porFornecedor = linhasComFornecedor
            .Where(p => p.FornecedorId.HasValue)
            .GroupBy(p => p.FornecedorId!.Value)
            .ToList();

        foreach (var g in porFornecedor)
        {
            var fornecedor = await fornecedorRepository.GetByIdAsync(command.EmpresaId, g.Key);
            var nome = fornecedor?.Nome ?? "Fornecedor desconhecido";
            var subtotalGrupo = g.Sum(p => p.Linha.Subtotal ?? 0m);
            var algumComCusto = g.Any(p => p.Linha.Subtotal.HasValue);

            grupos.Add(new SugestaoPorFornecedorResult(
                FornecedorId: g.Key,
                FornecedorNome: nome,
                Linhas: g.Select(p => p.Linha).ToList(),
                SubtotalEstimado: algumComCusto ? subtotalGrupo : null));
        }

        // Bucket "Sem fornecedor preferido" no final
        var semFornecedor = linhasComFornecedor.Where(p => !p.FornecedorId.HasValue).ToList();
        if (semFornecedor.Count > 0)
        {
            var subtotalSF = semFornecedor.Sum(p => p.Linha.Subtotal ?? 0m);
            var algumComCustoSF = semFornecedor.Any(p => p.Linha.Subtotal.HasValue);
            grupos.Add(new SugestaoPorFornecedorResult(
                FornecedorId: null,
                FornecedorNome: "Sem fornecedor preferido",
                Linhas: semFornecedor.Select(p => p.Linha).ToList(),
                SubtotalEstimado: algumComCustoSF ? subtotalSF : null));
        }

        logger.LogInformation(
            "Preview sugestao compra: empresa {EmpresaId}, {GrupoCount} grupos, {LinhaCount} linhas, totalConhecido={Algum}",
            command.EmpresaId, grupos.Count, command.Insumos.Count, algumCustoConhecido);

        return new PreviewSugestaoCompraResult(
            PorFornecedor: grupos,
            TotalEstimado: algumCustoConhecido ? totalGeral : null);
    }
}
