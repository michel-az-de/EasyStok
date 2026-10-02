using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Agenda diária de uma rotina (N12): <c>ParametrosJson.agenda = { "horario": "HH:mm" }</c>, em hora de parede de Brasília.
/// Puro e sem estado: a "última execução" é o evento do dia (chave <see cref="Chave"/>), não uma tabela. A conta passa por
/// <see cref="HorarioBrasil"/> e nunca por <c>UtcNow.Date</c>, então a virada do dia UTC (21:00 de Brasília) não adianta
/// nem atrasa nada. O catch-up vale só dentro do mesmo dia local: um resumo de ontem na manhã seguinte engana, então o
/// dia perdido fica perdido.
/// </summary>
public static partial class AgendaDiariaLocal
{
    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex FormatoHorario();

    /// <summary>Lê <c>HH:mm</c> estrito (duas casas em cada parte). Qualquer outra coisa é recusada.</summary>
    public static bool TentarLerHorario(string? texto, out TimeOnly horario)
    {
        horario = default;
        if (texto is null || !FormatoHorario().IsMatch(texto)) return false;
        horario = new TimeOnly(int.Parse(texto[..2]), int.Parse(texto[3..]));
        return true;
    }

    public static TimeOnly LerHorario(string? texto) =>
        TentarLerHorario(texto, out var horario)
            ? horario
            : throw new FormatException($"Horário '{texto}' fora do formato HH:mm (00:00 a 23:59).");

    /// <summary>A rotina declara uma agenda (a chave <c>agenda</c> existe), mesmo que mal formada?</summary>
    public static bool TemAgenda(string? parametrosJson) => LerAgenda(parametrosJson) is not null;

    /// <summary>O horário da agenda dos parâmetros, ou <c>null</c> quando não há agenda ou o horário é inválido.</summary>
    public static TimeOnly? HorarioDosParametros(string? parametrosJson)
    {
        var agenda = LerAgenda(parametrosJson);
        if (agenda is not { ValueKind: JsonValueKind.Object } objeto) return null;
        if (!objeto.TryGetProperty("horario", out var horario) || horario.ValueKind != JsonValueKind.String) return null;
        return TentarLerHorario(horario.GetString(), out var lido) ? lido : null;
    }

    /// <summary>
    /// A rotina está devida: a hora de parede de Brasília já passou de <paramref name="horario"/> no dia local de
    /// <paramref name="agoraUtc"/>. Se o dia local já virou, o horário do dia anterior não conta.
    /// </summary>
    public static bool Devida(TimeOnly horario, DateTime agoraUtc) =>
        agoraUtc >= HorarioBrasil.InstanteUtc(DiaLocal(agoraUtc), horario);

    /// <summary>O dia civil de Brasília de <paramref name="agoraUtc"/>.</summary>
    public static DateOnly DiaLocal(DateTime agoraUtc) => HorarioBrasil.DataOperacional(agoraUtc);

    /// <summary><c>CorrelationId</c> do evento do dia: <c>agenda:{rotinaId:N}:{yyyyMMdd}</c> (48 caracteres, cabe em 64).</summary>
    public static string Chave(Guid rotinaId, DateOnly diaLocal) => $"agenda:{rotinaId:N}:{diaLocal:yyyyMMdd}";

    private static JsonElement? LerAgenda(string? parametrosJson)
    {
        if (string.IsNullOrWhiteSpace(parametrosJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(parametrosJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("agenda", out var agenda)
                ? agenda.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
