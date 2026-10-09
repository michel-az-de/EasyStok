using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class MensagemProgramadaRepository(EasyStockDbContext db) : IMensagemProgramadaRepository
{
    public Task AddAsync(MensagemProgramada mensagem, CancellationToken ct = default) =>
        db.MensagensProgramadas.AddAsync(mensagem, ct).AsTask();

    public Task<MensagemProgramada?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.MensagensProgramadas.FirstOrDefaultAsync(m => m.EmpresaId == empresaId && m.Id == id, ct);

    public Task<MensagemProgramada?> ObterComLockAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.MensagensProgramadas.FromSqlInterpolated($"""
            SELECT * FROM mensagens_programadas
            WHERE "EmpresaId" = {empresaId} AND "Id" = {id}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<MensagemProgramada>> ListarAsync(
        Guid empresaId, Guid? clienteId, SituacaoMensagemProgramada? situacao, int limite, CancellationToken ct = default)
    {
        var query = db.MensagensProgramadas.AsNoTracking().Where(m => m.EmpresaId == empresaId);
        if (clienteId is { } c) query = query.Where(m => m.ClienteId == c);
        if (situacao is { } s) query = query.Where(m => m.Situacao == s);
        return await query.OrderByDescending(m => m.AgendadaPara).Take(Math.Clamp(limite, 1, 500)).ToListAsync(ct);
    }

    // SQL cru de propósito: FOR UPDATE SKIP LOCKED não sai do LINQ. IgnoreQueryFilters mantém o SQL
    // sem composição (o filtro global de tenant o embrulharia) — o disparador é cross-tenant e roda
    // com bypass de RLS ligado pelo host.
    public async Task<IReadOnlyList<MensagemProgramada>> ListarVencidasComLockAsync(DateTime agoraUtc, int limite, CancellationToken ct = default) =>
        await db.MensagensProgramadas
            .FromSqlInterpolated($"""
                SELECT * FROM mensagens_programadas
                WHERE "Situacao" = {(int)SituacaoMensagemProgramada.Agendada} AND "AgendadaPara" <= {agoraUtc}
                ORDER BY "AgendadaPara"
                LIMIT {limite}
                FOR UPDATE SKIP LOCKED
                """)
            .IgnoreQueryFilters()
            .ToListAsync(ct);
}
