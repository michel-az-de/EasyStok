using Cronos;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

public sealed class RotinaScheduler
{
    /// <summary>
    /// Calcula a próxima ocorrência de uma rotina Cron a partir de <paramref name="de"/>.
    /// Retorna null para rotinas do tipo Evento (disparadas on-demand).
    /// </summary>
    public DateTime? ProximaExecucao(RotinaNotificacao rotina, DateTime de)
    {
        if (rotina.TriggerTipo != TriggerTipoRotina.Cron || rotina.CronExpression is null)
            return null;

        var expr = CronExpression.Parse(rotina.CronExpression, CronFormat.Standard);
        return expr.GetNextOccurrence(de, TimeZoneInfo.Utc);
    }

    /// <summary>
    /// Indica se a rotina Cron deveria ter disparado entre <paramref name="ultimaExecucao"/>
    /// e <paramref name="agora"/>. Rotinas do tipo Evento retornam false (são event-driven).
    /// </summary>
    public bool DeveriasExecutar(RotinaNotificacao rotina, DateTime ultimaExecucao, DateTime agora)
    {
        if (!rotina.Ativa)
            return false;

        if (rotina.TriggerTipo == TriggerTipoRotina.Evento)
            return false;

        if (rotina.CronExpression is null)
            return false;

        var expr = CronExpression.Parse(rotina.CronExpression, CronFormat.Standard);
        var proxima = expr.GetNextOccurrence(ultimaExecucao, TimeZoneInfo.Utc);
        return proxima.HasValue && proxima.Value <= agora;
    }
}
