using System;

namespace EasyStock.Domain.Entities
{
    public class AdminImpersonationLog
    {
        public Guid Id { get; set; }
        public Guid AdminUsuarioId { get; set; }
        public Guid EmpresaId { get; set; }
        public DateTime InicioEm { get; set; }
        public DateTime? FimEm { get; set; }
        public string Ip { get; set; } = null!;

        public Usuario? AdminUsuario { get; set; }
        public Empresa? Empresa { get; set; }

        public static AdminImpersonationLog Criar(Guid adminUsuarioId, Guid empresaId, string ip)
        {
            return new AdminImpersonationLog
            {
                Id = Guid.NewGuid(),
                AdminUsuarioId = adminUsuarioId,
                EmpresaId = empresaId,
                InicioEm = DateTime.UtcNow,
                Ip = ip
            };
        }
    }
}
