namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IAuditLogRepository
    {
        Task AddAsync(AuditLog auditLog);
        Task<IEnumerable<AuditLog>> GetByUsuarioIdAsync(Guid usuarioId, int page, int pageSize);
    }
}