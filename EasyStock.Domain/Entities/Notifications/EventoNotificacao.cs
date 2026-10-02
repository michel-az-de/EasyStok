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

    /// <summary>
    /// O evento passou do prazo de validade do tipo sem ser avaliado (N1, quarentena): terminal, sem gerar mensagem.
    /// <paramref name="purgarPayload"/> apaga o payload dos tipos que carregam segredo (ver
    /// <see cref="PurgarPayload"/>).
    /// </summary>
    public void MarcarComoExpirado(string motivo, bool purgarPayload = false)
    {
        Status = StatusEventoNotificacao.Expirado;
        ErroProcessamento = motivo;
        ProcessadoEm = DateTime.UtcNow;
        if (purgarPayload) PurgarPayload();
    }

    public void MarcarComoFalhado(string erro)
    {
        Status = StatusEventoNotificacao.Falhado;
        ErroProcessamento = erro;
        ProcessadoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// Troca o payload por um objeto vazio (N2). O payload de um evento de segurança carrega o segredo (token ou
    /// código) e o fallback de canal ainda o relê, então o dispatcher só chama isto quando termina a última
    /// mensagem aberta do evento.
    /// </summary>
    public void PurgarPayload()
    {
        PayloadJson = "{}";
    }
}
