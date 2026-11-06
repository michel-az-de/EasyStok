namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IRefreshTokenRepository
    {
        Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
        Task<IEnumerable<RefreshToken>> GetByUsuarioIdAsync(Guid usuarioId);
        Task AddAsync(RefreshToken refreshToken);
        Task UpdateAsync(RefreshToken refreshToken);
        Task DeleteAsync(Guid id);
        /// <summary>Apaga em lote (<c>ExecuteDelete</c>) os refresh tokens que expiraram antes de <paramref name="expiradosAntesDe"/>, revogados ou não.</summary>
        Task<int> DeleteExpiredAsync(DateTime expiradosAntesDe, CancellationToken ct = default);
        Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId);
        Task<int> RevogarSessoesAtivasAsync(Guid usuarioId, DateTime agora);
        Task<int> RevogarSessoesWebAsync(Guid usuarioId, DateTime agora);
        Task AdicionarSessaoNativaAsync(SessaoNativa sessao);
        Task<SessaoNativa?> ObterSessaoNativaAsync(Guid id);
        /// <summary>Exige transação explícita; refresh e logout usam a mesma trava.</summary>
        Task<SessaoNativa?> ObterSessaoNativaComLockAsync(Guid id);
        Task<bool> ConsumirAsync(Guid id, DateTime agora);
        Task RevogarSessaoNativaAsync(Guid id, DateTime agora);
        Task<bool> DispositivoRevogadoAsync(string dispositivoId);
        /// <summary>Exige transação; login nativo e revogação administrativa serializam pelo aparelho existente.</summary>
        Task TravarDispositivoAsync(string dispositivoId);
        /// <summary>Substitui o único recibo da família na mesma transação da rotação; nulos apagam o recibo anterior.</summary>
        Task SalvarReciboRenovacaoAsync(Guid sessaoId, Guid? requisicaoId, Guid? refreshAnteriorId, string? sucessorProtegido);
    }
}
