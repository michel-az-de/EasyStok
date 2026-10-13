using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public class AuditLogRepository(EasyStockDbContext context) : IAuditLogRepository
{
    private readonly EasyStockDbContext _context = context;

    public Task AddAsync(AuditLog auditLog)
    {
        _context.AuditLogs.Add(auditLog);
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<AuditLog>> GetByUsuarioIdAsync(Guid usuarioId, int page, int pageSize) =>
        await _context.AuditLogs
            .AsNoTracking()
            .Where(al => al.UsuarioId == usuarioId)
            .OrderByDescending(al => al.DataHora)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

    // audit_logs não tem empresa: o recorte é pelo vínculo de quem foi afetado (usuarios_empresas, sob o filtro do tenant).
    public async Task<(IReadOnlyList<AuditoriaDeAcessoLinha> Itens, int Total)> ListarPorEmpresaAsync(
        Guid empresaId, Guid? usuarioId, int page, int pageSize, CancellationToken ct = default)
    {
        var consulta = _context.AuditLogs.AsNoTracking()
            .Where(al => _context.UsuariosEmpresas.Any(ue => ue.UsuarioId == al.UsuarioId && ue.EmpresaId == empresaId));
        if (usuarioId is { } id) consulta = consulta.Where(al => al.UsuarioId == id);

        var total = await consulta.CountAsync(ct);
        var itens = await consulta
            .OrderByDescending(al => al.DataHora)
            // Desempate estável: duas linhas no mesmo instante não trocam de página entre uma busca e outra.
            .ThenByDescending(al => al.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(al => new AuditoriaDeAcessoLinha(
                al.Id, al.DataHora, al.Acao, al.Sucesso, al.UsuarioId, al.Usuario!.Nome, al.Detalhes))
            .ToListAsync(ct);
        return (itens, total);
    }
}
