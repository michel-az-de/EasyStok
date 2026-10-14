using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

public interface ILogisticaRepository
{
    Task TravarAsync(Guid empresaId, CancellationToken ct);
    Task<IReadOnlyList<LogisticaPedido>> PedidosAsync(Guid empresaId, DateOnly? dia, Guid? pedidoId, CancellationToken ct);
    Task<IReadOnlyList<DespachoLogistica>> DespachosAsync(Guid empresaId, CancellationToken ct);
    Task<DespachoLogistica?> DespachoAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task<CotacaoLogistica?> CotacaoAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task<PoliticaLogistica?> PoliticaAsync(Guid empresaId, CancellationToken ct);
    Task<bool> TemViagemAtivaAsync(Guid empresaId, IReadOnlyList<Guid> pedidoIds, CancellationToken ct);
    Task<Guid> CriarViagemAsync(Guid empresaId, string provedor, IReadOnlyList<Guid> pedidoIds, CancellationToken ct);
    Task AtualizarViagemAsync(DespachoLogistica despacho, string evento, CancellationToken ct);
    Task<decimal> LimiteSaidaAsync(Guid empresaId, DateTime instanteUtc, bool dinheiro, CancellationToken ct);
    void Adicionar(CotacaoLogistica cotacao);
    void Adicionar(DespachoLogistica despacho);
    void Adicionar(PoliticaLogistica politica);
    void RegistrarAvisoDemonstracao(Guid empresaId, Guid despachoId, string tipo, string payload);
}
