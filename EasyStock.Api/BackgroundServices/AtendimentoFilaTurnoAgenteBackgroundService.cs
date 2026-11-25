using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Consumidor da fila <see cref="FilaAtendimentoNomes.TurnoAgente"/> (S06): o webhook do WhatsApp
/// enfileira e este loop roda <see cref="ProcessarTurnoAgenteUseCase"/> fora da requisição, um escopo
/// de DI por job. Fica na Api porque a fila (<c>BackgroundQueueService</c>) é em memória e o webhook
/// que enfileira roda aqui. A fila some num restart (deploy, queda); por isso o start roda
/// <see cref="RecuperarTurnosPerdidosUseCase"/> antes do loop (#2283).
/// </summary>
public sealed class AtendimentoFilaTurnoAgenteBackgroundService(
    IServiceProvider serviceProvider,
    IQueueService queueService,
    IConfiguration configuration,
    ILogger<AtendimentoFilaTurnoAgenteBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromMilliseconds(
            configuration.GetValue("Atendimento:FilaTurnoAgente:PollingIntervalMs", defaultValue: 500));

        var paralelismo = Math.Max(1, configuration.GetValue("Atendimento:FilaTurnoAgente:Paralelismo", defaultValue: 4));
        var paralelismoPorLoja = Math.Clamp(
            configuration.GetValue("Atendimento:FilaTurnoAgente:ParalelismoPorLoja", defaultValue: 2), 1, paralelismo);
        var executor = new ExecutorPorChave(paralelismo, (conversaId, ex) =>
            logger.LogError(ex, "AtendimentoFilaTurnoAgenteBackgroundService: turno da conversa {ConversaId} falhou.", conversaId),
            paralelismoPorLoja);

        logger.LogInformation(
            "AtendimentoFilaTurnoAgenteBackgroundService iniciado — polling={Intervalo}, paralelismo={Paralelismo}, porLoja={PorLoja}.",
            intervalo, paralelismo, paralelismoPorLoja);

        await RecuperarTurnosPerdidosAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await queueService.ProcessQueueAsync<ProcessarTurnoAgenteJob>(
                    FilaAtendimentoNomes.TurnoAgente,
                    job =>
                    {
                        // #3291 (R3): a chave é a conversa e o grupo é a loja. Antes (#2863) a chave era a loja, porque dois
                        // turnos da mesma loja disputavam o agregado mensal UsoIa (ler, somar, gravar sem trava); com a
                        // gravação fora do turno, uma conversa lenta não segura as outras da loja. Dentro da conversa a
                        // ordem fica. O teto por loja limita o gasto em paralelo e a folga da cota suave (a conferência
                        // da cota não é atômica: no máximo "teto menos um" turnos passam juntos pelo limite).
                        executor.Agendar(job.ConversaId, job.EmpresaId, async ct =>
                        {
                            using var scope = serviceProvider.CreateScope();
                            var useCase = scope.ServiceProvider.GetRequiredService<ProcessarTurnoAgenteUseCase>();
                            await useCase.ExecuteAsync(job, ct);
                        }, stoppingToken);
                        return Task.CompletedTask;
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AtendimentoFilaTurnoAgenteBackgroundService: erro na rodada, segue no próximo tick.");
            }

            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        await AguardarTurnosEmCursoAsync(executor);
    }

    /// <summary>Espera os turnos em curso no desligamento, com teto curto; loga quantos ficaram para trás.</summary>
    private async Task AguardarTurnosEmCursoAsync(ExecutorPorChave executor)
    {
        var teto = TimeSpan.FromSeconds(5);
        await Task.WhenAny(executor.AguardarTudoAsync(), Task.Delay(teto));

        var pendentes = executor.TrabalhosPendentes;
        if (pendentes > 0)
        {
            logger.LogWarning(
                "AtendimentoFilaTurnoAgenteBackgroundService: desligamento sem esperar {Pendentes} turno(s) em curso.",
                pendentes);
        }
    }

    /// <summary>
    /// #2283: devolve à fila o turno que o restart apagou. A consulta lê todas as lojas com o bypass de RLS, como o
    /// reenvio de mensagens; cada turno recuperado roda depois no tenant dele, pelo <see cref="ProcessarTurnoAgenteUseCase"/>.
    /// Falha aqui não impede o consumidor de subir.
    /// </summary>
    private async Task RecuperarTurnosPerdidosAsync(CancellationToken ct)
    {
        var janela = TimeSpan.FromMinutes(
            configuration.GetValue("Atendimento:FilaTurnoAgente:RecuperacaoJanelaMinutos", defaultValue: 30));
        try
        {
            using var scope = serviceProvider.CreateScope();
            using var _ = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().UseRowLevelSecurityBypass();
            await scope.ServiceProvider.GetRequiredService<RecuperarTurnosPerdidosUseCase>()
                .ExecuteAsync(DateTime.UtcNow, janela, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AtendimentoFilaTurnoAgenteBackgroundService: falha ao recuperar turnos perdidos no start.");
        }
    }
}
