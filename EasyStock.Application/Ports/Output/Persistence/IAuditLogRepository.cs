namespace EasyStock.Application.Ports.Output.Persistence
{
    /// <summary>Uma linha da auditoria de acesso já com o nome da pessoa afetada (#1753).</summary>
    public sealed record AuditoriaDeAcessoLinha(
        Guid Id, DateTime DataHora, string Acao, bool Sucesso, Guid UsuarioId, string UsuarioNome, string? Detalhes);

    public interface IAuditLogRepository
    {
        Task AddAsync(AuditLog auditLog);
        Task<IEnumerable<AuditLog>> GetByUsuarioIdAsync(Guid usuarioId, int page, int pageSize);
        /// <summary>Auditoria de acesso dos usuários vinculados à empresa, da mais recente para a mais antiga.</summary>
        Task<(IReadOnlyList<AuditoriaDeAcessoLinha> Itens, int Total)> ListarPorEmpresaAsync(
            Guid empresaId, Guid? usuarioId, int page, int pageSize, CancellationToken ct = default);
    }
}
