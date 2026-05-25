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
        // Justificativa do operador (LGPD compliance) — separado de Detalhes (técnico).
        public string? Motivo { get; private set; }
        // Quando a ação é sobre uma entidade dentro do tenant (Usuario/Loja/etc),
        // grava o id pra permitir filtro `me mostre tudo que foi feito no usuario X`.
        public Guid? EntidadeAfetadaId { get; private set; }
        public string? Ip { get; private set; }
        public DateTime CriadoEm { get; private set; }

        private AdminAuditLog() { }

        public static AdminAuditLog Criar(string adminEmail, string acao, string? detalhes = null,
                                          Guid? tenantId = null, string? ip = null,
                                          string? motivo = null, Guid? entidadeAfetadaId = null)
            => new()
            {
                Id = Guid.NewGuid(),
                AdminEmail = adminEmail,
                Acao = acao,
                Detalhes = detalhes,
                TenantId = tenantId,
                Motivo = motivo,
                EntidadeAfetadaId = entidadeAfetadaId,
                Ip = ip,
                CriadoEm = DateTime.UtcNow
            };
    }
}
