using System;

namespace EasyStock.Domain.Entities
{
    public class AdminAuditLog
    {
        public Guid Id { get; private set; }
        public string AdminEmail { get; private set; } = null!;
        public string Acao { get; private set; } = null!;
        public string? Detalhes { get; private set; }
        public Guid? TenantId { get; private set; }
        public string? Ip { get; private set; }
        public DateTime CriadoEm { get; private set; }

        private AdminAuditLog() { }

        public static AdminAuditLog Criar(string adminEmail, string acao, string? detalhes = null,
                                          Guid? tenantId = null, string? ip = null)
            => new()
            {
                Id = Guid.NewGuid(),
                AdminEmail = adminEmail,
                Acao = acao,
                Detalhes = detalhes,
                TenantId = tenantId,
                Ip = ip,
                CriadoEm = DateTime.UtcNow
            };
    }
}
