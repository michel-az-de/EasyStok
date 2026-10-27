namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IUsuarioEmpresaRepository
    {
        Task AddAsync(UsuarioEmpresa usuarioEmpresa);
        Task<UsuarioEmpresa?> GetByUsuarioEEmpresaAsync(Guid usuarioId, Guid empresaId);
        Task<IReadOnlyList<UsuarioEmpresa>> GetByUsuarioIdAsync(Guid usuarioId);
        Task UpdateAsync(UsuarioEmpresa usuarioEmpresa);

        /// <summary>
        /// Trava da hierarquia da empresa (#2070): <c>pg_advisory_xact_lock(hashtextextended('hierarquia:'||EmpresaId,0))</c>,
        /// que só solta no fim da transação. Exige transação aberta (<c>ExecuteInTransactionSemRetryAsync</c>): fora dela
        /// soltaria na hora, em silêncio. Liga também a empresa travada na RLS desta transação (<c>set_config</c> local), para
        /// as releituras depois da trava enxergarem o vínculo dela mesmo quando o suporte age a partir de outra empresa.
        /// </summary>
        Task TravarHierarquiaAsync(Guid empresaId, CancellationToken ct = default);

        /// <summary>
        /// Donas ativas da empresa (#2070): <c>COUNT(DISTINCT UsuarioId)</c> com <c>EmpresaId</c> explícito, marca, vínculo e
        /// usuário ativos e perfil de nível Admin na empresa: o predicado de <c>ParticipanteDaHierarquia.EhDonaAtiva</c>.
        /// </summary>
        Task<int> ContarDonasAtivasAsync(Guid empresaId, CancellationToken ct = default);

        /// <summary>
        /// Grava a marca de dona do vínculo num UPDATE só, com <c>EmpresaId</c> explícito. Só quem passou pela política de
        /// hierarquia chama, depois da trava. Devolve as linhas alteradas: 0 quando a marca já era essa ou não há vínculo.
        /// </summary>
        Task<int> DefinirDonaAsync(Guid usuarioId, Guid empresaId, bool dona, CancellationToken ct = default);
    }
}
