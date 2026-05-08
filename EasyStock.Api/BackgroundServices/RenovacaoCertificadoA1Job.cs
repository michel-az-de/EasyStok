using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Avisa o dono da empresa 30/15/7/3 dias antes do certificado A1 expirar.
/// Roda 1x/dia. Publica eventos via outbox para o módulo Notifications.
/// </summary>
public sealed class RenovacaoCertificadoA1Job(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<BackgroundJobOptions> opts,
    ILogger<RenovacaoCertificadoA1Job> log) : BackgroundService
{
    private static readonly int[] MarcosDias = [30, 15, 7, 3];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("RenovacaoCertificadoA1Job iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (opts.CurrentValue.Nfce.RenovacaoCertificadoEnabled)
                {
                    await ProcessarRodadaAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log.LogError(ex, "Erro em RenovacaoCertificadoA1Job.");
            }

            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ProcessarRodadaAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICertificadoA1Repository>();
        var eventos = scope.ServiceProvider.GetRequiredService<IPublicadorEventoIntegracao>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var expirando = await repo.ListarExpirandoAsync(diasAhead: 30, ct);
        if (expirando.Count == 0) return;

        log.LogInformation("{Count} certificado(s) A1 expirando em <30d", expirando.Count);

        var agora = DateTime.UtcNow;
        foreach (var cert in expirando)
        {
            var diasRestantes = (cert.ValidoAte - agora).TotalDays;
            var marco = MarcosDias.FirstOrDefault(m => diasRestantes <= m);
            if (marco == 0) continue;

            await eventos.PublicarAsync(
                empresaId: cert.EmpresaId,
                tipoEvento: $"nfce.certificado.expirando.{marco}",
                aggregateType: nameof(EasyStock.Domain.Entities.Fiscal.NotaFiscalCertificadoA1),
                aggregateId: cert.Id,
                payload: new
                {
                    certificadoId = cert.Id,
                    empresaId = cert.EmpresaId,
                    nomeTitular = cert.NomeTitular,
                    validoAte = cert.ValidoAte,
                    diasRestantes,
                    marco,
                },
                ct: ct);
        }

        await uow.CommitAsync();
    }
}
