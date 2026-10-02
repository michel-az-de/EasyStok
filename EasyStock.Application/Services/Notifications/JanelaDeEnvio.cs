using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Janela diária de envio da rotina (N5), pura e no fuso de Brasília. O intervalo é <c>[inicio, fim)</c> e vira a
/// meia-noite quando <c>inicio &gt; fim</c>. O resultado serve para adiar a mensagem, nunca para suprimi-la.
/// </summary>
public static class JanelaDeEnvio
{
    /// <summary>
    /// Próxima abertura em UTC quando <paramref name="agoraUtc"/> está fora da janela; <c>null</c> quando está dentro ou
    /// quando a janela não existe (alguma ponta nula, ou pontas iguais).
    /// </summary>
    public static DateTime? ProximaAbertura(TimeOnly? inicio, TimeOnly? fim, DateTime agoraUtc)
    {
        if (inicio is not { } abre || fim is not { } fecha || abre == fecha) return null;

        var local = HorarioBrasil.ConverterParaBrasilia(agoraUtc);
        var hora = TimeOnly.FromDateTime(local);
        var dentro = abre < fecha
            ? hora >= abre && hora < fecha
            : hora >= abre || hora < fecha;
        if (dentro) return null;

        var dia = DateOnly.FromDateTime(local);
        if (hora >= abre) dia = dia.AddDays(1);
        return HorarioBrasil.InstanteUtc(dia, abre);
    }

    /// <summary><c>Seguranca</c> ignora a janela: o código de acesso não pode esperar a manhã.</summary>
    public static DateTime? ProximaAbertura(
        TimeOnly? inicio, TimeOnly? fim, DateTime agoraUtc, CategoriaConteudoNotificacao categoria) =>
        categoria == CategoriaConteudoNotificacao.Seguranca ? null : ProximaAbertura(inicio, fim, agoraUtc);
}
