namespace EasyStock.Application.UseCases.ListarUsuarios
{
    /// <summary>
    /// Estado do convite de primeiro acesso (N9) para a tela da dona: <c>pendente</c> (ainda com o marcador no lugar da
    /// senha), <c>aceito</c> (com o instante e a via já mascarada: <c>+55•••1234</c>, <c>e-mail</c> ou <c>Google</c>) ou
    /// <c>nenhum</c> (criado com senha). O telefone inteiro nunca sai daqui, e o hash também não.
    /// </summary>
    public sealed record ConviteDoUsuario(string Estado, DateTime? AceitoEm, string? Via)
    {
        public const string Pendente = "pendente";
        public const string Aceito = "aceito";
        public const string Nenhum = "nenhum";

        public static ConviteDoUsuario De(Usuario u) =>
            u.ConvitePendente ? new(Pendente, null, null)
            : u.ConviteAceitoEm is { } em ? new(Aceito, em, u.ConviteAceitoVia)
            : new(Nenhum, null, null);
    }

    public sealed record UsuarioResult(
        Guid UsuarioId,
        string Nome,
        string Email,
        bool Ativo,
        DateTime? UltimoAcessoEm,
        DateTime CriadoEm,
        string Nivel,
        ConviteDoUsuario Convite);

    public sealed record ListarUsuariosQuery(Guid EmpresaId, int Page = 1, int PageSize = 20);

    public class ListarUsuariosUseCase(IUsuarioRepository usuarioRepository)
    {
        public async Task<(IEnumerable<UsuarioResult> Usuarios, int Total)> ExecuteAsync(ListarUsuariosQuery query)
        {
            var (usuarios, total) = await usuarioRepository.GetByEmpresaAsync(query.EmpresaId, query.Page, query.PageSize);
            return (usuarios.Select(ToResult), total);
        }

        private static UsuarioResult ToResult(Usuario u) =>
            new(u.Id, u.Nome, u.Email, u.Ativo, u.UltimoAcessoEm, u.CriadoEm,
                u.Perfis?.OrderBy(p => p.Perfil?.Nivel).FirstOrDefault()?.Perfil?.Nivel.ToString() ?? "Operador",
                ConviteDoUsuario.De(u));
    }
}
