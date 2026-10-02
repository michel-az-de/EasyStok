using EasyStock.Application.Services.Auth;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Limpeza agendada dos segredos de acesso (N8, #301): a cada 6 h apaga em lote os <c>reset_tokens</c> expirados há mais de
/// 24 h, usados ou não (o <c>DeleteExpiredAsync</c> antigo não tinha chamador e carregava tudo em memória). A regra está em
/// <see cref="LimpezaTokensAcesso"/>; aqui só o ritmo e o escopo de DI por rodada. Falha numa rodada não derruba o host.
/// <c>Auth:LimpezaTokens:Habilitada=false</c> desliga sem deploy; <c>IntervaloHoras</c> muda o ritmo.
/// </summary>
public sealed class LimpezaTokensAcessoService(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<LimpezaTokensAcessoService> logger) : BackgroundService
{
    public const double IntervaloPadraoEmHoras = 6;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Auth:LimpezaTokens:Habilitada", true))
        {
            logger.LogInformation("LimpezaTokensAcessoService desligado por Auth:LimpezaTokens:Habilitada=false.");
            return;
        }

        var intervalo = TimeSpan.FromHours(configuration.GetValue("Auth:LimpezaTokens:IntervaloHoras", IntervaloPadraoEmHoras));
        if (intervalo <= TimeSpan.Zero) intervalo = TimeSpan.FromHours(IntervaloPadraoEmHoras);

        // Deixa o app subir antes da primeira rodada.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var escopo = scopes.CreateScope();
                await escopo.ServiceProvider.GetRequiredService<LimpezaTokensAcesso>().ExecutarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "LimpezaTokensAcessoService: falha na rodada; a proxima sai em {Intervalo}.", intervalo);
            }

            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
