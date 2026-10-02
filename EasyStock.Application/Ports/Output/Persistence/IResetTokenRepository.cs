namespace EasyStock.Application.Ports.Output.Persistence
{
    /// <summary>Quantos pedidos de redefinição a conta fez (linhas <c>Reset</c> de <c>reset_tokens</c>, N8).</summary>
    public sealed record ContagemPedidosReset(int NaUltimaHora, int NasUltimas24Horas, DateTime? UltimoPedidoEm);

    public interface IResetTokenRepository
    {
        /// <summary>Procura pelo hash SHA-256 do texto. Quem chama confere a <see cref="ResetToken.Finalidade"/>.</summary>
        Task<ResetToken?> GetByTokenAsync(string token);

        /// <summary>O segredo aberto (não usado, não expirado) mais recente do usuário para a finalidade.</summary>
        Task<ResetToken?> ObterAbertoAsync(Guid usuarioId, string finalidade, DateTime agora);
        Task<IEnumerable<ResetToken>> GetByUsuarioIdAsync(Guid usuarioId);
        Task AddAsync(ResetToken resetToken);
        Task UpdateAsync(ResetToken resetToken);
        Task DeleteAsync(Guid id);
        Task<int> DeleteAllByUsuarioIdAsync(Guid usuarioId);

        /// <summary>
        /// Marca como usados, num UPDATE só, os segredos abertos do usuário (<c>Reset</c> e <c>ResetCodigo</c>). Imediato,
        /// como o <c>RevogarSessoesAtivasAsync</c>: chame antes de criar o novo. Devolve as linhas alteradas.
        /// </summary>
        Task<int> InvalidarAbertosAsync(Guid usuarioId, DateTime agora);

        /// <summary>
        /// Uso único de verdade: <c>UPDATE ... SET Usado = true WHERE Id = @id AND Usado = false AND ExpiraEm &gt; @agora</c>.
        /// Só <c>true</c> (1 linha afetada) autoriza trocar a senha; o perdedor de uma corrida recebe <c>false</c>.
        /// </summary>
        Task<bool> ConsumirAsync(Guid id, DateTime agora);

        /// <summary>
        /// Gasta uma tentativa do código: <c>UPDATE ... SET Tentativas = Tentativas + 1 WHERE Id = @id AND Usado = false AND
        /// ExpiraEm &gt; @agora AND Tentativas &lt; 5</c>. Devolve o valor novo de <c>Tentativas</c>, ou 0 quando nenhuma
        /// tentativa foi gasta (esgotado, usado ou expirado).
        /// </summary>
        Task<int> RegistrarTentativaAsync(Guid id, DateTime agora);

        /// <summary>Pedidos <c>Reset</c> da conta na última hora e nas últimas 24 h, e o instante do último (limites da N8).</summary>
        Task<ContagemPedidosReset> ContarPedidosAsync(Guid usuarioId, DateTime agora);

        /// <summary>Apaga em lote (<c>ExecuteDelete</c>) os segredos que expiraram antes de <paramref name="expiradosAntesDe"/>, usados ou não.</summary>
        Task<int> ApagarExpiradosAsync(DateTime expiradosAntesDe, CancellationToken ct = default);
    }
}
