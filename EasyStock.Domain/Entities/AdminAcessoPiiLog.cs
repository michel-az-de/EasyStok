using System;

namespace EasyStock.Domain.Entities;

/// <summary>
/// Log dedicado de toda visualização de PII em texto-claro pelo operador admin
/// (LGPD compliance — Art. 37 "registro das operações de tratamento"). Separado
/// de <see cref="AdminAuditLog"/> pra permitir relatório ANPD em segundos sem
/// grep em logs livres. Cada clique em "👁 ver email completo" gera 1 linha.
/// </summary>
public class AdminAcessoPiiLog
{
    public Guid Id { get; private set; }
    /// <summary>Email do operador (pego do JWT). Não é Guid pq pode ser "system" em jobs.</summary>
    public string AdminEmail { get; private set; } = null!;
    public Guid? TenantId { get; private set; }
    /// <summary>"usuario" / "empresa" / "loja" — quem foi inspecionado.</summary>
    public string EntidadeTipo { get; private set; } = null!;
    public Guid EntidadeId { get; private set; }
    /// <summary>"email" / "documento" / "telefone" — qual campo foi desmascarado.</summary>
    public string Campo { get; private set; } = null!;
    public string? Motivo { get; private set; }
    public string? Ip { get; private set; }
    public DateTime CriadoEm { get; private set; }

    private AdminAcessoPiiLog() { }

    public static AdminAcessoPiiLog Criar(
        string adminEmail,
        string entidadeTipo,
        Guid entidadeId,
        string campo,
        string? motivo = null,
        Guid? tenantId = null,
        string? ip = null)
        => new()
        {
            Id = Guid.NewGuid(),
            AdminEmail = adminEmail,
            EntidadeTipo = entidadeTipo,
            EntidadeId = entidadeId,
            Campo = campo,
            Motivo = motivo,
            TenantId = tenantId,
            Ip = ip,
            CriadoEm = DateTime.UtcNow
        };
}
