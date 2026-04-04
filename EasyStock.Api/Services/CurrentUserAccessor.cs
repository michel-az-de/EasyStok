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
            if (user?.Identity?.IsAuthenticated != true)
                return false;

            var permissionClaims = user.FindAll("permissao")
                .Select(x => x.Value)
                .Concat(user.FindAll("permissoes")
                    .SelectMany(x => x.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));

            foreach (var claim in permissionClaims)
            {
                if (Enum.TryParse<Permissao>(claim, true, out var parsed) && parsed == permissao)
                    return true;
            }

            return Nivel switch
            {
                NivelAcesso.SuperAdmin => true,
                NivelAcesso.Admin => true,
                NivelAcesso.Gerente => permissao is not Permissao.GerenciarUsuarios,
                NivelAcesso.Operador => permissao is Permissao.GerenciarEstoque or Permissao.GerenciarProdutos,
                _ => permissao is Permissao.VisualizarRelatorios
            };
        }
    }
}
