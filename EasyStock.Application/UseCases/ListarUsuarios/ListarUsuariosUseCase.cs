using System.Text.RegularExpressions;

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

    /// <param name="Ativo">#1753: o acesso NESTA empresa (o vínculo), que é o que "desativar" desliga.</param>
    /// <param name="PerfilId">O perfil que o login usa nesta empresa; nulo para quem ficou sem perfil.</param>
    public sealed record UsuarioResult(
        Guid UsuarioId,
        string Nome,
        string Email,
        bool Ativo,
        DateTime? UltimoAcessoEm,
        DateTime CriadoEm,
        string Nivel,
        ConviteDoUsuario Convite,
        Guid? PerfilId = null,
        string? Perfil = null);

    public sealed record ListarUsuariosQuery(Guid EmpresaId, int Page = 1, int PageSize = 20);

    public class ListarUsuariosUseCase(IUsuarioRepository usuarioRepository)
    {
        public async Task<(IEnumerable<UsuarioResult> Usuarios, int Total)> ExecuteAsync(ListarUsuariosQuery query)
        {
            var (usuarios, total) = await usuarioRepository.GetByEmpresaAsync(query.EmpresaId, query.Page, query.PageSize);
            return (usuarios.Select(u => ToResult(u, query.EmpresaId)), total);
        }

        private static UsuarioResult ToResult(Usuario u, Guid empresaId)
        {
            // O mesmo critério do login: o perfil de menor nível (mais poder) na empresa.
            var perfil = u.Perfis?.OrderBy(p => p.Perfil?.Nivel).FirstOrDefault()?.Perfil;
            // Sem o vínculo carregado (lista antiga, teste) vale o cadastro; com ele, vale o acesso à empresa.
            var vinculo = u.Empresas?.FirstOrDefault(e => e.EmpresaId == empresaId);
            return new(u.Id, u.Nome, u.Email, u.Ativo && (vinculo?.Ativo ?? true), u.UltimoAcessoEm, u.CriadoEm,
                perfil?.Nivel.ToString() ?? "Operador", ConviteDoUsuario.De(u), perfil?.Id, perfil?.Nome);
        }
    }

    public sealed record PerfilAtribuivel(Guid PerfilId, string Nome, string Nivel, string? ModuloInicial);

    /// <summary>#1753: os perfis que a dona pode dar a alguém da empresa. O perfil de suporte nunca aparece.</summary>
    public class ListarPerfisAtribuiveisUseCase(IPerfilRepository perfilRepository)
    {
        public async Task<IReadOnlyList<PerfilAtribuivel>> ExecuteAsync(Guid empresaId) =>
            (await perfilRepository.GetByEmpresaAsync(empresaId))
                .Where(p => p.Nivel != NivelAcesso.SuperAdmin)
                .OrderBy(p => p.Nivel).ThenBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new PerfilAtribuivel(p.Id, p.Nome, p.Nivel.ToString(), p.ModuloInicial))
                .ToList();
    }

    public sealed record ListarAuditoriaDeAcessoQuery(Guid EmpresaId, Guid? UsuarioId = null, int Page = 1, int PageSize = 50);

    /// <summary>
    /// #1753: a dona lê quem entrou, quem foi convidado, desativado ou trocou de perfil. Linhas antigas guardaram o
    /// identificador de quem fez; aqui ele vira o nome, e o que não é da empresa some do texto.
    /// </summary>
    public partial class ListarAuditoriaDeAcessoUseCase(IAuditLogRepository auditLogRepository, IUsuarioRepository usuarioRepository)
    {
        public async Task<(IReadOnlyList<AuditoriaDeAcessoLinha> Itens, int Total)> ExecuteAsync(
            ListarAuditoriaDeAcessoQuery query, CancellationToken ct = default)
        {
            var pagina = Math.Max(1, query.Page);
            var tamanho = Math.Clamp(query.PageSize, 1, 100);
            var (itens, total) = await auditLogRepository.ListarPorEmpresaAsync(query.EmpresaId, query.UsuarioId, pagina, tamanho, ct);
            if (!itens.Any(i => i.Detalhes is not null && Identificador().IsMatch(i.Detalhes))) return (itens, total);

            var (usuarios, _) = await usuarioRepository.GetByEmpresaAsync(query.EmpresaId, 1, 500);
            var nomes = usuarios.ToDictionary(u => u.Id, u => u.Nome);
            return (itens.Select(i => i.Detalhes is null ? i : i with
            {
                Detalhes = Identificador().Replace(i.Detalhes, m => nomes.GetValueOrDefault(Guid.Parse(m.Value), "suporte")),
            }).ToList(), total);
        }

        [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
        private static partial Regex Identificador();
    }
}
