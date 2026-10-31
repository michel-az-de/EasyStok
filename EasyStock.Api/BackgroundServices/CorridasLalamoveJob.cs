using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Consulta de contingência das corridas da Lalamove (#2160, R6). O webhook chega fora de ordem, pode não chegar, e
/// a Lalamove desativa a URL depois de 10 falhas: a cada 2 min, cada corrida da API em curso é reconsultada pelo mesmo
/// <see cref="SincronizarCorridaUseCase"/> do webhook. Também é quem dá como não criada a corrida que ficou
/// "solicitando" depois de um timeout. Advisory lock: só um host roda a rodada.
/// Desligado por <c>BackgroundJobs:EnableCorridasLalamove=false</c>.
/// </summary>
public sealed class CorridasLalamoveJob(IServiceProvider serviceProvider, ILogger<CorridasLalamoveJob> logger) : BackgroundService
{
    private const long LockKeyJob = 0x436F7272694C616CL; // "CorriLal"
    private const int MaximoPorRodada = 100;
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(50), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RodarComTravaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "CorridasLalamoveJob: erro na rodada.");
            }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RodarComTravaAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetService<EasyStockDbContext>();
        if (db is null || !db.Database.IsNpgsql()) return;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var conn = db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@k)";
            var p = cmd.CreateParameter(); p.ParameterName = "k"; p.Value = LockKeyJob; cmd.Parameters.Add(p);
            if ((bool?)await cmd.ExecuteScalarAsync(ct) != true) return;
            try { await ProcessarAsync(ct); }
            finally
            {
                await using var rel = conn.CreateCommand();
                rel.CommandText = "SELECT pg_advisory_unlock(@k)";
                var rp = rel.CreateParameter(); rp.ParameterName = "k"; rp.Value = LockKeyJob; rel.Parameters.Add(rp);
                await rel.ExecuteScalarAsync(ct);
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private async Task ProcessarAsync(CancellationToken ct)
    {
        IReadOnlyList<CorridaLocalizada> emCurso;
        using (var scope = serviceProvider.CreateScope())
            emCurso = await scope.ServiceProvider.GetRequiredService<ICorridaExternaRepository>().ListarEmCursoAsync(MaximoPorRodada, ct);
        if (emCurso.Count == 0) return;

        var falhas = 0;
        foreach (var corrida in emCurso)
        {
            // Um escopo por corrida: DbContext limpo e o tenant da corrida só nele. Uma falha não pula as outras.
            try
            {
                using var scope = serviceProvider.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(corrida.EmpresaId);
                await scope.ServiceProvider.GetRequiredService<SincronizarCorridaUseCase>().ExecuteAsync(corrida.EmpresaId, corrida.CorridaId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                falhas++;
                logger.LogWarning(ex, "CorridasLalamoveJob: corrida {CorridaId} não sincronizou.", corrida.CorridaId);
            }
        }
        logger.LogInformation("CorridasLalamoveJob: em curso={Total} falhas={Falhas}", emCurso.Count, falhas);
    }
}
