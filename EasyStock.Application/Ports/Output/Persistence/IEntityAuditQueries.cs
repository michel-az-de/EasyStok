namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Read model da auditoria universal de entidades (entity_alteracoes). Concentra
/// as timelines + resumo que viviam direto no EntityAuditController (F7). O filtro
/// multi-tenant (empresaId) é responsabilidade do chamador.
/// </summary>
public interface IEntityAuditQueries
{
    Task<(IReadOnlyList<EntityAuditEntry> Items, int Total)> PorEntidadeAsync(
        Guid empresaId, string tipoEntidade, Guid entidadeId, int page, int pageSize, CancellationToken ct = default);

    Task<(IReadOnlyList<ClientTimelineEntry> Items, int Total)> TimelineClienteAsync(
        Guid empresaId, Guid clienteId, int page, int pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<EntityAuditTipoContagem>> ResumoPorTipoAsync(
        Guid empresaId, CancellationToken ct = default);
}

public sealed record EntityAuditEntry(
    Guid Id,
    string Acao,
    string? Campo,
    string? ValorAntigo,
    string? ValorNovo,
    string? AlteradoPorNome,
    string? Origem,
    DateTime AlteradoEm,
    bool HasPii);

public sealed record ClientTimelineEntry(
    Guid Id,
    string TipoEntidade,
    Guid EntidadeId,
    string Acao,
    string? Campo,
    string? ValorAntigo,
    string? ValorNovo,
    string? AlteradoPorNome,
    string? Origem,
    DateTime AlteradoEm);

public sealed record EntityAuditTipoContagem(string Tipo, int Count);
