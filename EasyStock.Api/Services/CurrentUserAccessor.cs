using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using System.Security.Claims;

namespace EasyStock.Api.Services
{
    public sealed class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
    {
        private const string EmpresaIdClaim = "empresaId";
        private const string UsuarioIdClaim = "sub";
        private const string NivelClaim = "nivel";
        private const string PermissaoClaim = "permissao";

        public Guid EmpresaId => GetGuidClaimOrDefault(EmpresaIdClaim);

        public bool IsAuthenticated =>
            CurrentUser?.Identity?.IsAuthenticated == true;

        public Guid UsuarioId => GetGuidClaimOrDefault(UsuarioIdClaim);

        public NivelAcesso Nivel => GetNivelClaimOrDefault();

        public string? Ip
        {
            get
            {
                var ctx = httpContextAccessor.HttpContext;
                if (ctx is null) return null;
                // Honra X-Forwarded-For (proxy/load balancer) antes de cair em RemoteIpAddress.
                var fwd = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(fwd))
                    return fwd.Split(',')[0].Trim();
                return ctx.Connection.RemoteIpAddress?.ToString();
            }
        }

        public string? UserAgent
        {
            get
            {
                var ua = httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].FirstOrDefault();
                return string.IsNullOrWhiteSpace(ua) ? null : ua.Length > 500 ? ua[..500] : ua;
            }
        }

        public string? DispositivoId =>
            httpContextAccessor.HttpContext?.Request.Headers["X-Device-Id"].FirstOrDefault();

        public bool TemPermissao(Permissao permissao)
        {
            if (!IsAuthenticated)
                return false;

            var permissoesExplicitas = GetPermissaoClaims();

            // Se tem claims explicitas de permissao, usar somente elas.
            if (permissoesExplicitas.Count > 0)
                return permissoesExplicitas.Contains(permissao);

            // Sem claims de permissao, aplicar fallback por nivel.
            return Nivel switch
            {
                NivelAcesso.SuperAdmin => true,
                NivelAcesso.Admin => permissao is not Permissao.ConfigurarSla,
                NivelAcesso.Gerente => permissao is not Permissao.GerenciarUsuarios
                    and not Permissao.ConfigurarSla,
                NivelAcesso.Operador => permissao is Permissao.GerenciarEstoque or Permissao.GerenciarProdutos
                    or Permissao.VisualizarTickets or Permissao.ResponderTickets
                    or Permissao.ResponderTicketsInternos,
                _ => permissao is Permissao.VisualizarRelatorios or Permissao.VisualizarTickets
            };
        }

        private ClaimsPrincipal? CurrentUser => httpContextAccessor.HttpContext?.User;

        private Guid GetGuidClaimOrDefault(string claimType)
        {
            var claimValue = CurrentUser?.FindFirstValue(claimType);
            return Guid.TryParse(claimValue, out var id) ? id : Guid.Empty;
        }

        private NivelAcesso GetNivelClaimOrDefault()
        {
            var claimValue = CurrentUser?.FindFirstValue(NivelClaim);
            return Enum.TryParse<NivelAcesso>(claimValue, out var nivel)
                ? nivel
                : NivelAcesso.Visualizador;
        }

        private HashSet<Permissao> GetPermissaoClaims()
        {
            var values = CurrentUser?
                .FindAll(PermissaoClaim)
                .Select(static claim => claim.Value)
                .ToList();

            if (values is not { Count: > 0 })
                return [];

            var permissoes = new HashSet<Permissao>();

            foreach (var value in values)
            {
                if (Enum.TryParse<Permissao>(value, ignoreCase: true, out var permissao))
                    permissoes.Add(permissao);
            }

            return permissoes;
        }
    }
}
