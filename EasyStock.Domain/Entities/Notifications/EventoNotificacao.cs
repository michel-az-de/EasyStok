using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Domain.Entities.Notifications;

public class EventoNotificacao
{
    public Guid Id { get; set; }
    public TipoEventoNotificacao Tipo { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid? RefEntidadeId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime OcorridoEm { get; set; }
    public DateTime? ProcessadoEm { get; set; }
    public StatusEventoNotificacao Status { get; set; } = StatusEventoNotificacao.Pendente;
    public string CorrelationId { get; set; } = string.Empty;
    public string? ErroProcessamento { get; set; }

    public Empresa? Empresa { get; set; }

    public static EventoNotificacao Criar(
        TipoEventoNotificacao tipo,
        Guid empresaId,
        string payloadJson,
        Guid? refEntidadeId = null,
        string? correlationId = null) => new()
    {
        Id = Guid.NewGuid(),
        Tipo = tipo,
        EmpresaId = empresaId,
        RefEntidadeId = refEntidadeId,
        PayloadJson = payloadJson,
        OcorridoEm = DateTime.UtcNow,
        Status = StatusEventoNotificacao.Pendente,
        CorrelationId = correlationId ?? Guid.NewGuid().ToString("N")
    };

    public void MarcarComoProcessado()
    {
        Status = StatusEventoNotificacao.Processado;
        ProcessadoEm = DateTime.UtcNow;
    }

    public void MarcarComoFalhado(string erro)
    {
        Status = StatusEventoNotificacao.Falhado;
        ErroProcessamento = erro;
        ProcessadoEm = DateTime.UtcNow;
    }
}
