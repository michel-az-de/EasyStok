using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

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
}
