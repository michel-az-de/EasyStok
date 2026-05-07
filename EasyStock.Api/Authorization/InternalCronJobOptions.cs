namespace EasyStock.Api.Authorization;

/// <summary>
/// Configuração dos endpoints internos de gatilho HTTP.
/// Lida via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> para
/// permitir hot-reload (rotação de token sem restart, habilitar/desabilitar dinâmico).
/// O <see cref="Token"/> deve SEMPRE vir de secret store / env var
/// (NOTIFICATIONS__CRONJOB__TOKEN), nunca do appsettings.json em produção.
/// </summary>
public sealed class InternalCronJobOptions
{
    public const string Section = "Notifications:CronJob";

    public bool Habilitado { get; set; }
    public string? Token { get; set; }
}
