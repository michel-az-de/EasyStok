using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

/// <summary>Expediente da loja por empresa (S40). Ausência de registro = <see cref="ExpedienteLoja.CriarPadrao"/>.</summary>
public interface IExpedienteLojaRepository
{
    /// <summary>Console autenticado: rastreado para atualização.</summary>
    Task<ExpedienteLoja?> GetByEmpresaIdAsync(Guid empresaId, CancellationToken ct = default);

    /// <summary>
    /// Checkout público (sem JWT): mesma leitura que o storefront faz pelo slug — ignora o filtro
    /// global e filtra por <paramref name="empresaId"/> no WHERE (ADR-0010).
    /// </summary>
    Task<ExpedienteLoja?> GetPublicoAsync(Guid empresaId, CancellationToken ct = default);

    Task AddAsync(ExpedienteLoja expediente, CancellationToken ct = default);
    Task UpdateAsync(ExpedienteLoja expediente, CancellationToken ct = default);
}
