using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface ICadastrosFinanceirosRepository
{
    Task<IReadOnlyList<ContaBancaria>> ListarContasAsync(Guid empresaId, CancellationToken ct);
    Task<ContaBancaria?> ObterContaAsync(Guid empresaId, Guid id, CancellationToken ct);
    void Adicionar(ContaBancaria conta);
    Task<IReadOnlyList<MetodoPagamentoEmpresa>> ListarMetodosAsync(Guid empresaId, CancellationToken ct);
    Task<MetodoPagamentoEmpresa?> ObterMetodoAsync(Guid empresaId, string codigo, CancellationToken ct);
    void Adicionar(MetodoPagamentoEmpresa metodo);
}
