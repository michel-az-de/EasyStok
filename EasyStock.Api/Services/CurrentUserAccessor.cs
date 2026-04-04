using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using System.Security.Claims;

namespace EasyStock.Api.Services
{
    public sealed class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
    {
        public Guid EmpresaId
        {
            get
            {
                var claim = httpContextAccessor.HttpContext?.User.FindFirstValue("empresaId");
                return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
            }
        }

        public bool IsAuthenticated =>
            httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

        public Guid UsuarioId
        {
            get
            {
                var claim = httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
                return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
            }
        }

        public NivelAcesso Nivel
        {
            get
            {
                var claim = httpContextAccessor.HttpContext?.User.FindFirstValue("nivel");
                return Enum.TryParse<NivelAcesso>(claim, out var nivel) ? nivel : NivelAcesso.Visualizador;
            }
        }

        public bool TemPermissao(Permissao permissao)
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return false;

            var permissaoClaims = user.FindAll("permissao").Select(x => x.Value).ToList();

            // Se tem claims explícitas de permissão, usar SOMENTE elas (autoridade do Perfil)
            if (permissaoClaims.Count > 0)
                return permissaoClaims.Any(c =>
                    Enum.TryParse<Permissao>(c, true, out var p) && p == permissao);

            // Sem claims de permissão (ex: autenticado sem empresa) → fallback por Nivel
            return Nivel switch
            {
                NivelAcesso.SuperAdmin or NivelAcesso.Admin => true,
                NivelAcesso.Gerente => permissao is not Permissao.GerenciarUsuarios,
                NivelAcesso.Operador => permissao is Permissao.GerenciarEstoque or Permissao.GerenciarProdutos,
                _ => permissao is Permissao.VisualizarRelatorios
            };
        }
    }
}
