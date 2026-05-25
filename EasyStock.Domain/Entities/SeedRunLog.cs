namespace EasyStock.Domain.Entities;

/// <summary>
/// Registro de auditoria de cada execução de seed. Persiste:
/// - quem rodou, quando, qual tipo/volume
/// - progresso step-by-step (JSON)
/// - backup dos IDs deletados (JSON) pra recuperação emergencial
/// - resultado final (Success | Failed | RolledBack)
/// </summary>
public class SeedRunLog
{
    public Guid Id { get; set; }

    /// <summary>Email do super admin que disparou o seed (extraído do JWT).</summary>
    public string AdminEmail { get; set; } = "";

    /// <summary>adminTestScenarios | demo | minimal</summary>
    public string TipoSeed { get; set; } = "";

    /// <summary>small | medium | large — só relevante pra tipo=demo.</summary>
    public string? Volume { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Running | Success | Failed | RolledBack</summary>
    public string Status { get; set; } = "Running";

    /// <summary>JSON array de SeedEtapa — log step-by-step do que foi feito.</summary>
    public string? EtapasJson { get; set; }

    /// <summary>JSON com IDs das entidades deletadas antes de recriar (backup emergencial).</summary>
    public string? BackupJson { get; set; }

    /// <summary>Mensagem de erro se Status=Failed ou RolledBack.</summary>
    public string? Erro { get; set; }

    /// <summary>Resumo compacto pra listagem: 4 empresas, 12 usuários, 3 lojas criados.</summary>
    public string? Resumo { get; set; }
}
