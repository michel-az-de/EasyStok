using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Notifications.Hosting;

/// <summary>
/// Limites do <see cref="NotificacoesBacklogHealthCheck"/> (seção <c>Notifications:Health</c>).
/// </summary>
public sealed class NotificacoesHealthOptions
{
    public const string Section = "Notifications:Health";

    /// <summary>Acima disto o <c>Pendente</c> elegível (ou o evento pendente) mais antigo deixa o check <c>Unhealthy</c>. Padrão 5 min.</summary>
    public int PendenteElegivelMaxMinutos { get; set; } = 5;

    /// <summary>A partir de quantos <c>Falhado</c> na última hora o check fica <c>Degraded</c>. Padrão 5.</summary>
    public int FalhadoPorHoraMax { get; set; } = 5;

    /// <summary>Acima de quantos <c>Indeterminado</c> na última hora o check fica <c>Degraded</c>. Padrão 0: qualquer um.</summary>
    public int IndeterminadoPorHoraMax { get; set; }

    /// <summary>
    /// Acima de quantos eventos <c>Processado</c> sem outbox em 24 h o check fica <c>Degraded</c>. Sem padrão (desligado):
    /// evento sem rotina ou sem canal permitido fecha assim de propósito, então o número só vale como dado.
    /// </summary>
    public int? ProcessadoSemOutboxMax { get; set; }
}

/// <summary>
/// Health do backlog do motor de notificações (N1, ver e avisar). Mede a idade do <c>Pendente</c> elegível mais antigo, o
/// <c>EmEnvio</c> além do lease, <c>Falhado</c>, <c>Simulado</c>, <c>Indeterminado</c> e <c>Expirado</c> por hora e o
/// <c>Processado</c> sem outbox em 24 h; e, em Production, o provider ativo de teste (<c>stub</c> ou <c>console</c>).
/// <list type="bullet">
/// <item><c>Unhealthy</c>: backlog parado (pendente elegível acima do limite, evento pendente acima do limite, <c>EmEnvio</c> além do lease).</item>
/// <item><c>Degraded</c>: o motor anda mas não entrega (falhas por hora, <c>Indeterminado</c>, <c>Simulado</c> em Production, provider de teste em Production).</item>
/// </list>
/// Vai para <c>/health/notificacoes</c> (tag <c>notificacoes</c>), fora de <c>/health</c> e <c>/health/ready</c>: um backlog
/// ruim não tira a API do balanceador. A medida vem de consulta agregada sob bypass pela porta, sem trazer linha.
/// </summary>
public sealed class NotificacoesBacklogHealthCheck(
    IBacklogNotificacoes backlog,
    IOptions<NotificacoesHealthOptions> options,
    IHostEnvironment environment,
    IConfiguration configuration,
    IServiceProvider services) : IHealthCheck
{
    /// <summary>Tag do check: o endpoint dedicado e o ping do Worker o selecionam por ela.</summary>
    public const string Tag = "notificacoes";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var limites = options.Value;
        var medida = await backlog.MedirAsync(cancellationToken);
        var producao = environment.IsProduction();
        var parados = new List<string>();
        var avisos = new List<string>();
        var limitePendente = TimeSpan.FromMinutes(limites.PendenteElegivelMaxMinutos);

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["pendente_elegivel_idade_segundos"] = (int)(medida.IdadeDoPendenteElegivelMaisAntigo?.TotalSeconds ?? 0),
            ["evento_pendente_idade_segundos"] = (int)(medida.IdadeDoEventoPendenteMaisAntigo?.TotalSeconds ?? 0),
            ["em_envio_alem_do_lease"] = medida.EmEnvioAlemDoLease,
            ["falhado_ultima_hora"] = medida.FalhadoNaUltimaHora,
            ["simulado_ultima_hora"] = medida.SimuladoNaUltimaHora,
            ["indeterminado_ultima_hora"] = medida.IndeterminadoNaUltimaHora,
            ["expirado_ultima_hora"] = medida.ExpiradoNaUltimaHora,
            ["processado_sem_outbox_24h"] = medida.ProcessadoSemOutboxEm24h,
        };

        if (medida.IdadeDoPendenteElegivelMaisAntigo is { } pendente && pendente > limitePendente)
            parados.Add($"Pendente elegível há {(int)pendente.TotalMinutes} min (limite {limites.PendenteElegivelMaxMinutos} min)");
        if (medida.IdadeDoEventoPendenteMaisAntigo is { } evento && evento > limitePendente)
            parados.Add($"Evento pendente há {(int)evento.TotalMinutes} min sem avaliar (limite {limites.PendenteElegivelMaxMinutos} min)");
        if (medida.EmEnvioAlemDoLease > 0)
            parados.Add($"{medida.EmEnvioAlemDoLease} mensagem(ns) EmEnvio além do lease");

        if (medida.FalhadoNaUltimaHora >= limites.FalhadoPorHoraMax)
            avisos.Add($"{medida.FalhadoNaUltimaHora} Falhado na última hora (limite {limites.FalhadoPorHoraMax})");
        if (medida.IndeterminadoNaUltimaHora > limites.IndeterminadoPorHoraMax)
            avisos.Add($"{medida.IndeterminadoNaUltimaHora} Indeterminado na última hora");
        if (producao && medida.SimuladoNaUltimaHora > 0)
            avisos.Add($"{medida.SimuladoNaUltimaHora} Simulado na última hora em Production (nada saiu)");
        if (limites.ProcessadoSemOutboxMax is { } semOutbox && medida.ProcessadoSemOutboxEm24h > semOutbox)
            avisos.Add($"{medida.ProcessadoSemOutboxEm24h} evento(s) Processado sem outbox em 24 h");
        if (producao)
            avisos.AddRange(ProvidersDeTeste());

        if (parados.Count > 0)
            return HealthCheckResult.Unhealthy(string.Join("; ", parados.Concat(avisos)), data: data);
        if (avisos.Count > 0)
            return HealthCheckResult.Degraded(string.Join("; ", avisos), data: data);
        return HealthCheckResult.Healthy("Backlog do motor de notificações dentro dos limites.", data);
    }

    /// <summary>Canais cujo provider ativo não envia nada: <c>stub</c> (WhatsApp e SMS, o padrão sem configuração) e o e-mail de console.</summary>
    private IEnumerable<string> ProvidersDeTeste()
    {
        var whatsapp = configuration["Notifications:WhatsApp:Provider"] ?? "stub";
        if (string.Equals(whatsapp, "stub", StringComparison.OrdinalIgnoreCase))
            yield return $"WhatsApp: provider {whatsapp} em Production";

        var sms = configuration["Notifications:Sms:Provider"] ?? "stub";
        if (string.Equals(sms, "stub", StringComparison.OrdinalIgnoreCase))
            yield return $"SMS: provider {sms} em Production";

        if (services.GetService<IEmailService>() is IEmailServiceSimulado email)
            yield return $"E-mail: provider {email.Provider} em Production";
    }
}
