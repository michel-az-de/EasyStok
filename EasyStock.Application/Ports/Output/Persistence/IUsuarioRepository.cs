namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IUsuarioRepository
    {
        Task<Usuario?> GetByIdAsync(Guid id);
        Task<Usuario?> GetByEmailAsync(string email);

        /// <summary>#1324: usuários com e-mail <c>{parteLocal}+…@gmail.com</c>, carregados como no login.</summary>
        Task<IReadOnlyList<Usuario>> ListarPorAliasGmailAsync(string parteLocal);
        Task<(IEnumerable<Usuario> Usuarios, int Total)> GetByEmpresaAsync(Guid empresaId, int page, int pageSize);
        Task<int> CountByEmpresaAsync(Guid empresaId);
        Task AddAsync(Usuario usuario);

        /// <summary>
        /// Grava os campos escalares da linha, exceto <c>SessoesValidasDesde</c>: o corte das sessões só muda
        /// por <see cref="AtualizarSessoesValidasDesdeAsync"/>, para um login que leu o usuário antes de uma
        /// revogação não desfazê-la (#1352).
        /// </summary>
        Task UpdateAsync(Usuario usuario);
        Task<IEnumerable<Usuario>> SearchAsync(Guid empresaId, string termo, int maxResults = 20);

        /// <summary>
        /// #1352: projeção leve que o validador do JWT lê a cada requisição (cache de 60 s por cima). A tabela
        /// <c>usuarios</c> não tem <c>EmpresaId</c>: a leitura roda sem tenant e sem bypass de RLS.
        /// Devolve nulo quando o usuário não existe.
        /// </summary>
        Task<SessaoDoUsuario?> ObterSessaoAsync(Guid usuarioId);

        /// <summary>
        /// #1352: grava o corte das sessões num UPDATE atômico que nunca recua (só vale se for posterior ao
        /// que já está gravado), imediato como o <c>RevogarSessoesAtivasAsync</c> dos refresh tokens.
        /// </summary>
        /// <returns>Linhas alteradas: 1 quando o corte avançou, 0 quando já havia um igual ou posterior.</returns>
        Task<int> AtualizarSessoesValidasDesdeAsync(Guid usuarioId, DateTime desde);
    }
}
