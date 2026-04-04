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

        public bool TemPermissao(Permissao permissao) => false;
    }
}
