using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Publica o evento <c>IncidenteSistema</c> (N10, ADR-0057). Monta o payload só com textos fixos em pt-BR a partir de enums
/// fechados, resolve a empresa padrão (N13), liga o tenant dela no escopo e enfileira com a chave
/// <c>incidente:{componente}:{estado}:{janela}</c>, em que <c>janela = unixSegundos / (JanelaDedupeMinutos * 60)</c>. A chave
/// vira o <c>IdempotencyKey</c> do outbox (junto do destinatário, N4): a dedupe de 15 min não precisa de tabela nova.
/// Limite honesto: a janela é fixa, não deslizante; uma falha que atravessa a virada pode gerar dois avisos em menos de 15 min.
/// Configuração: <c>Notifications:Incidentes:Habilitado</c> (padrão true) e <c>Notifications:Incidentes:JanelaDedupeMinutos</c> (15).
/// </summary>
public sealed class PublicadorIncidenteSistema(
    IConfiguration configuration,
    IEmpresaPadraoResolver empresaPadrao,
    INotificadorService notificador,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<PublicadorIncidenteSistema> logger) : IPublicadorIncidenteSistema
{
    public const string ChaveHabilitado = "Notifications:Incidentes:Habilitado";
    public const string ChaveJanela = "Notifications:Incidentes:JanelaDedupeMinutos";
    public const int JanelaPadraoMinutos = 15;

    public async Task PublicarAsync(
        ComponenteIncidente componente,
        EstadoIncidente estado,
        SeveridadeIncidente severidade,
        DateTime desdeUtc,
        CancellationToken ct = default)
    {
        if (!Habilitado())
        {
            logger.LogDebug("Avisos de incidente desligados ({Chave}=false): {Componente} {Estado} não publicado.",
                ChaveHabilitado, componente, estado);
            return;
        }

        var empresaId = await empresaPadrao.ResolverAsync(ct);
        if (empresaId is not { } empresa || empresa == Guid.Empty)
        {
            logger.LogWarning(
                "Aviso de incidente {Componente} {Estado} não publicado: sem empresa padrão. Configure {Chave}.",
                componente, estado, EmpresaPadraoResolver.Chave);
            return;
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        var chave = ChaveDeDedupe(componente, estado, agora, JanelaMinutos());
        var payload = new Dictionary<string, string>
        {
            ["componente"] = TextoDe(componente),
            ["estado_texto"] = TextoDe(estado),
            ["gravidade"] = TextoDe(severidade),
            ["desde"] = HorarioBrasil.ConverterParaBrasilia(desdeUtc).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            ["duracao"] = TextoDuracao(agora - desdeUtc),
            [NotificadorService.ChaveIdempotenciaPayload] = chave,
        };

        tenantContext.SetCurrentTenant(empresa);
        await notificador.EnfileirarEventoAsync(
            TipoEventoNotificacao.IncidenteSistema, empresa, JsonSerializer.Serialize(payload), ct: ct, correlationId: chave);
        try
        {
            await unitOfWork.CommitAsync();
        }
        catch (Exception ex) when (unitOfWork.EhViolacaoDeUnicidade(ex))
        {
            // N12: o índice único (EmpresaId, CorrelationId) recusa o mesmo incidente na mesma janela. É a dedupe.
            logger.LogDebug("Incidente {Componente} {Estado} já publicado nesta janela ({Chave}).", componente, estado, chave);
        }
    }

    /// <summary><c>incidente:{componente}:{estado}:{janela}</c>; cabe no <c>varchar(64)</c> do <c>CorrelationId</c>.</summary>
    public static string ChaveDeDedupe(ComponenteIncidente componente, EstadoIncidente estado, DateTime agoraUtc, int janelaMinutos)
    {
        var segundos = new DateTimeOffset(DateTime.SpecifyKind(agoraUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var janela = segundos / (Math.Max(1, janelaMinutos) * 60L);
        return $"incidente:{componente.ToString().ToLowerInvariant()}:{estado.ToString().ToLowerInvariant()}:{janela}";
    }

    private int JanelaMinutos()
    {
        return int.TryParse(configuration[ChaveJanela], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutos) && minutos > 0
            ? minutos
            : JanelaPadraoMinutos;
    }

    private bool Habilitado() => !bool.TryParse(configuration[ChaveHabilitado], out var ligado) || ligado;

    private static string TextoDe(ComponenteIncidente componente) => componente switch
    {
        ComponenteIncidente.Api => "API do EasyStok",
        ComponenteIncidente.Banco => "Banco de dados",
        ComponenteIncidente.Redis => "Cache (Redis)",
        ComponenteIncidente.Erros5xx => "Erros no servidor (5xx)",
        ComponenteIncidente.Integracao => "Integração externa",
        _ => "Sistema",
    };

    private static string TextoDe(EstadoIncidente estado) =>
        estado == EstadoIncidente.Normalizado ? "normalizado" : "com problema";

    private static string TextoDe(SeveridadeIncidente severidade) => severidade switch
    {
        SeveridadeIncidente.Critica => "Crítica",
        SeveridadeIncidente.Alta => "Alta",
        _ => "Média",
    };

    private static string TextoDuracao(TimeSpan duracao)
    {
        var minutos = (long)Math.Floor(duracao.TotalMinutes);
        if (minutos < 1) return "menos de 1 minuto";
        if (minutos < 60) return minutos == 1 ? "1 minuto" : $"{minutos} minutos";
        var horas = minutos / 60;
        var resto = minutos % 60;
        var h = horas == 1 ? "1 hora" : $"{horas} horas";
        return resto == 0 ? h : $"{h} e {resto} min";
    }
}
