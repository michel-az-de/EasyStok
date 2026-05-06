using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Domain.Entities.Notifications;

public class BloqueioNotificacao
{
    public Guid Id { get; set; }
    public Guid? EmpresaId { get; set; }
    public CanalNotificacao? Canal { get; set; }
    public string Motivo { get; set; } = null!;
    public DateTime AtivadoEm { get; set; }
    public string AtivadoPor { get; set; } = null!;
    public DateTime? ExpiraEm { get; set; }
    public DateTime? RemovidoEm { get; set; }
    public string? RemovidoPor { get; set; }

    public Empresa? Empresa { get; set; }

    public static BloqueioNotificacao Criar(
        string motivo,
        string ativadoPor,
        Guid? empresaId = null,
        CanalNotificacao? canal = null,
        DateTime? expiraEm = null) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        Canal = canal,
        Motivo = motivo,
        AtivadoEm = DateTime.UtcNow,
        AtivadoPor = ativadoPor,
        ExpiraEm = expiraEm
    };

    public void Remover(string removidoPor)
    {
        RemovidoEm = DateTime.UtcNow;
        RemovidoPor = removidoPor;
    }

    public bool EstaAtivo(DateTime referencia)
    {
        if (RemovidoEm.HasValue) return false;
        if (ExpiraEm.HasValue && referencia >= ExpiraEm.Value) return false;
        return true;
    }
}
