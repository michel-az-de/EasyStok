using EasyStock.Domain.Events;

namespace EasyStock.Domain.Financeiro.Events;

/// <summary>
/// Domain event publicado a cada baixa registrada. Consumidores: integracao
/// contabil (partida dobrada caixa x natureza), conciliacao bancaria, dashboard
/// financeiro. <see cref="StatusResultante"/> indica se o lancamento ficou
/// parcial ou totalmente quitado apos esta baixa.
/// </summary>
public sealed record LancamentoBaixadoEvent(
    Guid EventoId,
    DateTime OcorridoEm,
    Guid EmpresaId,
    Guid LancamentoId,
    Guid BaixaId,
    decimal ValorBaixado,
    decimal ValorRestante,
    StatusLancamento StatusResultante,
    string MeioPagamento,
    Guid? ContaBancariaId,
    string? ChaveExterna) : DomainEvent(EventoId, OcorridoEm);
