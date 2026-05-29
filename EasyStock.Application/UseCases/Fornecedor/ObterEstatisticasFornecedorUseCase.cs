namespace EasyStock.Application.UseCases.Fornecedor;

public sealed record ObterEstatisticasFornecedorQuery(Guid EmpresaId, Guid FornecedorId);

public class ObterEstatisticasFornecedorUseCase(
    IFornecedorRepository fornecedorRepository,
    IPedidoFornecedorRepository pedidoFornecedorRepository)
{
    public async Task<FornecedorEstatisticasResult> ExecuteAsync(ObterEstatisticasFornecedorQuery query)
    {
        var fornecedor = await fornecedorRepository.GetByIdAsync(query.EmpresaId, query.FornecedorId);
        if (fornecedor is null)
            throw new UseCaseValidationException("Fornecedor nao encontrado.");

        var estatisticas = await pedidoFornecedorRepository.GetEstatisticasAsync(query.EmpresaId, query.FornecedorId);

        return new FornecedorEstatisticasResult(
            query.FornecedorId,
            estatisticas.TotalGasto,
            estatisticas.QuantidadePedidos,
            estatisticas.LeadTimeRealMedioDias,
            estatisticas.FrequenciaPedidosPorMes);
    }
}
