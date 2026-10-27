using System.Linq.Expressions;

namespace EasyStock.Domain.Entities
{
    public class Perfil
    {
        public Guid Id { get; set; }
        public Guid? EmpresaId { get; set; }
        public string Nome { get; set; } = null!;
        public string? Descricao { get; set; }
        public string? ModuloInicial { get; set; }
        public bool PermissoesExplicitas { get; set; }
        public NivelAcesso Nivel { get; set; }
        public DateTime CriadoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public ICollection<PerfilPermissao> Permissoes { get; set; } = new List<PerfilPermissao>();
        public ICollection<UsuarioPerfil> Usuarios { get; set; } = new List<UsuarioPerfil>();

        /// <summary>
        /// B1 (#2070): o suporte da plataforma é o perfil global, sem empresa, de nível SuperAdmin. É o único lugar que
        /// compara o nível SuperAdmin de um perfil (teste de arquitetura); as consultas do EF usam esta expressão.
        /// </summary>
        public static readonly Expression<Func<Perfil, bool>> SuporteGlobal =
            p => p.Nivel == NivelAcesso.SuperAdmin && p.EmpresaId == null;

        private static readonly Func<Perfil, bool> EhSuporteGlobalCompilado = SuporteGlobal.Compile();

        /// <summary>Perfil do suporte da plataforma (<see cref="SuporteGlobal"/>).</summary>
        public bool EhSuporteGlobal => EhSuporteGlobalCompilado(this);

        /// <summary>
        /// O nível que o perfil dá dentro de uma empresa. Perfil de empresa com nível SuperAdmin é dado inválido e vale
        /// Administração: a sessão nasce no nível da empresa e nunca abre <c>api/admin/*</c> (B1).
        /// </summary>
        public NivelAcesso NivelNaEmpresa => Nivel == NivelAcesso.SuperAdmin && !EhSuporteGlobal ? NivelAcesso.Admin : Nivel;

        /// <summary>
        /// <see cref="NivelNaEmpresa"/> igual a Admin, como expressão do EF: a contagem de donas ativas no banco usa a mesma
        /// regra da memória (#2070).
        /// </summary>
        public static readonly Expression<Func<Perfil, bool>> DaNivelAdminNaEmpresa =
            p => p.Nivel == NivelAcesso.Admin || (p.Nivel == NivelAcesso.SuperAdmin && p.EmpresaId != null);

        /// <summary>
        /// Perfil que a empresa pode dar a alguém: nunca o de nível SuperAdmin (de suporte ou não) e nunca o de outra empresa.
        /// </summary>
        public bool AtribuivelNaEmpresa(Guid empresaId) =>
            Nivel != NivelAcesso.SuperAdmin && (EmpresaId is null || EmpresaId == empresaId);
    }
}
