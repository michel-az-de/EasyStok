using System.Text.RegularExpressions;
using EasyStock.Application.Services.Auth;
using EasyStock.Domain.Services;

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

    /// <summary>
    /// O que quem está pedindo pode fazer com esta linha (#2070), pela <c>PoliticaDeHierarquia</c> e pelas pré-condições
    /// que a rota confere: a API devolve 2xx se e somente se a ação está aqui. Desativar, reativar, marcar e tirar a marca
    /// valem pela autoridade (repetir é um 2xx que não muda nada); o console escolhe o botão pelo estado da linha
    /// (<c>ativo</c> e <c>dona</c>). Trocar perfil: existe ao menos um nível que quem pede pode dar a esta pessoa (o
    /// <c>atribuivel</c> de cada perfil diz quais). Nome, e-mail e convite são da conta e olham todas as empresas dela.
    /// </summary>
    public sealed record AcoesSobreUsuario(
        bool EditarNome,
        bool TrocarEmail,
        bool TrocarPerfil,
        bool Desativar,
        bool Reativar,
        bool ReenviarConvite,
        bool MarcarDona,
        bool DesmarcarDona)
    {
        public static AcoesSobreUsuario Nenhuma { get; } = new(false, false, false, false, false, false, false, false);
    }

    /// <param name="Ativo">#1753: o acesso NESTA empresa (o vínculo), que é o que "desativar" desliga.</param>
    /// <param name="Nivel">O nível na empresa; sem perfil é Visualizador, como o login entra (B2, #2070).</param>
    /// <param name="PerfilId">O perfil que o login usa nesta empresa; nulo para quem ficou sem perfil.</param>
    /// <param name="Dona">A marca de dona do vínculo com esta empresa (#2070).</param>
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
        string? Perfil = null,
        bool Dona = false,
        AcoesSobreUsuario? Acoes = null);

    public sealed record ListarUsuariosQuery(Guid EmpresaId, int Page = 1, int PageSize = 20);

    public class ListarUsuariosUseCase(IUsuarioRepository usuarioRepository, HierarquiaDeUsuarios hierarquia)
    {
        public async Task<(IEnumerable<UsuarioResult> Usuarios, int Total)> ExecuteAsync(ListarUsuariosQuery query)
        {
            var (usuarios, total) = await usuarioRepository.GetByEmpresaAsync(query.EmpresaId, query.Page, query.PageSize);
            var pagina = usuarios.ToList();
            var ator = await hierarquia.AtorAsync(query.EmpresaId);
            var donas = await hierarquia.DonasAtivasAsync(query.EmpresaId);
            // A conta inteira de cada linha, com todas as empresas (a lista vem no recorte desta).
            var contas = (await usuarioRepository.GetByIdsAsync(pagina.Select(u => u.Id).ToList()) ?? [])
                .ToDictionary(u => u.Id);
            return (pagina.Select(u => ToResult(u, contas.GetValueOrDefault(u.Id, u), query.EmpresaId, ator, donas)).ToList(), total);
        }

        private static UsuarioResult ToResult(Usuario u, Usuario conta, Guid empresaId, ParticipanteDaHierarquia ator, int donas)
        {
            // O mesmo critério do login: o perfil de mais poder na empresa.
            var perfil = u.Perfis?.Where(p => p.Perfil is not null).Select(p => p.Perfil!).OrderBy(p => p.NivelNaEmpresa).FirstOrDefault();
            // Sem o vínculo carregado (lista antiga, teste) vale o cadastro; com ele, vale o acesso à empresa.
            var vinculo = u.Empresas?.FirstOrDefault(e => e.EmpresaId == empresaId);
            var alvo = ParticipanteDaHierarquia.De(u, empresaId, conta.EhSuperAdmin());
            return new(u.Id, u.Nome, u.Email, u.Ativo && (vinculo?.Ativo ?? true), u.UltimoAcessoEm, u.CriadoEm,
                alvo.Nivel.ToString(), ConviteDoUsuario.De(u), perfil?.Id, perfil?.Nome,
                vinculo?.EhDona ?? false, Acoes(ator, alvo, u, conta, donas));
        }

        /// <summary>Os níveis que um perfil de empresa dá; trocar perfil vale se ao menos um deles passa pela política.</summary>
        private static readonly NivelAcesso[] NiveisDeEmpresa =
            [NivelAcesso.Admin, NivelAcesso.Gerente, NivelAcesso.Operador, NivelAcesso.Visualizador];

        private static AcoesSobreUsuario Acoes(
            ParticipanteDaHierarquia ator, ParticipanteDaHierarquia alvo, Usuario u, Usuario conta, int donas)
        {
            var daConta = alvo.ComAContaInteira(conta);
            bool Pode(AcaoSobreUsuario acao, NivelAcesso? nivelNovo = null) =>
                PoliticaDeHierarquia.Avaliar(ator, PoliticaDeHierarquia.EhDaConta(acao) ? daConta : alvo, acao, donas, nivelNovo).Permitido;
            return new(
                EditarNome: Pode(AcaoSobreUsuario.EditarNome),
                // As mesmas pré-condições da rota: conta só desta empresa (ou o suporte), vínculo ativo para trocar o perfil e
                // para reenviar (a guarda de tenant do reenvio olha o vínculo ativo; o suporte passa sem ela).
                TrocarEmail: (ator.EhSuporteGlobal || HierarquiaDeUsuarios.EmailTrocavelPelaEmpresa(conta)) && Pode(AcaoSobreUsuario.TrocarEmail),
                TrocarPerfil: alvo.VinculoAtivo && NiveisDeEmpresa.Any(nivel => Pode(AcaoSobreUsuario.TrocarPerfil, nivel)),
                Desativar: Pode(AcaoSobreUsuario.Desativar),
                Reativar: Pode(AcaoSobreUsuario.Reativar),
                ReenviarConvite: (ator.EhSuporteGlobal || alvo.VinculoAtivo) && u.Ativo && u.ConvitePendente && !conta.EhSuperAdmin()
                    && Pode(AcaoSobreUsuario.ReenviarConvite),
                MarcarDona: Pode(AcaoSobreUsuario.MarcarDona),
                DesmarcarDona: Pode(AcaoSobreUsuario.DesmarcarDona));
        }
    }

    /// <param name="Atribuivel">#2070: quem está pedindo pode dar este perfil (a Administração não dá perfil de nível Admin).</param>
    public sealed record PerfilAtribuivel(Guid PerfilId, string Nome, string Nivel, string? ModuloInicial, bool Atribuivel = true);

    /// <summary>
    /// #1753: os perfis que a dona pode dar a alguém da empresa. O perfil de suporte nunca aparece. #2070: cada perfil diz
    /// se quem está pedindo pode dá-lo (<see cref="PoliticaDeHierarquia.PodeDarNivel"/>).
    /// </summary>
    public class ListarPerfisAtribuiveisUseCase(IPerfilRepository perfilRepository, HierarquiaDeUsuarios hierarquia)
    {
        public async Task<IReadOnlyList<PerfilAtribuivel>> ExecuteAsync(Guid empresaId)
        {
            var ator = await hierarquia.AtorAsync(empresaId);
            return (await perfilRepository.GetByEmpresaAsync(empresaId))
                .Where(p => p.AtribuivelNaEmpresa(empresaId))
                .OrderBy(p => p.Nivel).ThenBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new PerfilAtribuivel(p.Id, p.Nome, p.Nivel.ToString(), p.ModuloInicial,
                    PoliticaDeHierarquia.PodeDarNivel(ator, p.NivelNaEmpresa)))
                .ToList();
        }
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
