using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Email;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Entrada do atendimento por e-mail (#1432). A cada rodada (60 s por padrão): descobre num escopo com bypass de RLS
/// as empresas com caixa de suporte configurada e lê a caixa de cada uma num escopo próprio, com o tenant dela.
/// Fica no processo da API, junto dos outros loops do atendimento, porque o aviso ao console (SSE de operação) é em
/// memória: no Worker o publicador é nulo e a conversa só apareceria no próximo recarregar. Vários processos lendo a
/// mesma caixa não duplicam: o Message-ID é único por empresa no banco.
/// </summary>
public sealed class AtendimentoCaixaEmailBackgroundService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    TimeProvider relogio,
    ILogger<AtendimentoCaixaEmailBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _intervalo = Intervalo(configuration);
    private readonly RecuoCaixaEmail _recuo = new(Intervalo(configuration));

    /// <summary>E-mails por caixa por rodada: o resto fica para a próxima, sem segurar a rodada das outras empresas.</summary>
    public const int LotePorCaixa = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AtendimentoCaixaEmailBackgroundService iniciado — polling={Intervalo}.", _intervalo);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RodadaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AtendimentoCaixaEmailBackgroundService: erro na rodada — continua no próximo tick.");
            }

            try { await Task.Delay(_intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static TimeSpan Intervalo(IConfiguration configuration) => TimeSpan.FromSeconds(
        configuration.GetValue("Atendimento:CaixaEmail:PollingIntervalSeconds", defaultValue: 60));

    private async Task RodadaAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> empresas;
        using (var scope = serviceProvider.CreateScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().UseRowLevelSecurityBypass();
            empresas = await scope.ServiceProvider.GetRequiredService<IEmailAtendimentoQuery>().ListarEmpresasComCaixaAsync(ct);
        }

        foreach (var empresaId in empresas)
        {
            var agora = relogio.GetUtcNow().UtcDateTime;
            if (!_recuo.PodeLer(empresaId, agora))
                continue;
            try
            {
                using var scope = serviceProvider.CreateScope();
                var r = await scope.ServiceProvider.GetRequiredService<LerCaixaEmailAtendimentoUseCase>()
                    .ExecuteAsync(empresaId, LotePorCaixa, ct, _recuo.Quarentena(empresaId, agora));
                _recuo.RegistrarSucesso(empresaId);
                if (r.Gravados + r.Duplicados + r.Ignorados + r.Falhas > 0)
                    logger.LogInformation(
                        "Caixa de e-mail da empresa {EmpresaId}: {Gravados} gravados, {Duplicados} repetidos, {Ignorados} ignorados, {Falhas} falhas.",
                        empresaId, r.Gravados, r.Duplicados, r.Ignorados, r.Falhas);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // #1642 (item 3): senha trocada ou servidor fora não vira tentativa a cada rodada para sempre.
                var espera = _recuo.RegistrarFalha(empresaId, agora);
                logger.LogWarning(ex, "Caixa de e-mail da empresa {EmpresaId} não foi lida; próxima tentativa em {Espera}.",
                    empresaId, espera);
            }
        }
    }
}
