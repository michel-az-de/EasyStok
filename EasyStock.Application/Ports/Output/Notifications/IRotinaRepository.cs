using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IRotinaRepository
{
    Task<RotinaNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RotinaNotificacao?> GetByCodigoAsync(string codigo, Guid? empresaId, CancellationToken ct = default);

    /// <summary>
    /// Rotinas ativas. Com <paramref name="empresaId"/> (o motor), devolve as da empresa e as globais
    /// (<c>EmpresaId</c> nulo) ignorando o filtro do EF: a policy de leitura do catálogo (N1) deixa o tenant ler o
    /// global e o <c>WHERE</c> leva a empresa porque, no Worker, o filtro do EF está desligado e só a RLS isola.
    /// Sem empresa, mantém o filtro do EF (telas e varredura cross-tenant sob bypass).
    /// </summary>
    Task<IReadOnlyList<RotinaNotificacao>> ListarAtivasAsync(
        TipoEventoNotificacao? tipoEvento = null,
        Guid? empresaId = null,
        CancellationToken ct = default);

    Task<(IReadOnlyList<RotinaNotificacao> Items, int Total)> ListarAsync(
        Guid? empresaId,
        bool? ativa = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task AddAsync(RotinaNotificacao rotina, CancellationToken ct = default);
    Task UpdateAsync(RotinaNotificacao rotina, CancellationToken ct = default);
}
