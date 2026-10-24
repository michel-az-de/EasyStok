namespace EasyStock.Api.BackgroundServices;

/// <summary>Quantas empresas a rodada percorreu e em quantas o trabalho falhou.</summary>
public sealed record ResultadoRodadaPorEmpresa(int Empresas, int Falhas);

/// <summary>
/// Roda um trabalho de estoque uma vez por empresa, cada uma num escopo de DI próprio com o tenant ligado (#2016).
///
/// <para>
/// Fora de request o tenant é <c>Guid.Empty</c>: o filtro global do EF (<c>EmpresaId == CurrentTenantId</c>) e a RLS
/// zeram toda leitura de entidade com empresa, e o job acordava, consultava e não fazia nada (#1588). A lista de empresas
/// sai de um escopo sem tenant porque <c>empresas</c> não tem <c>EmpresaId</c> e fica fora do filtro e da RLS; cada empresa
/// abre o próprio escopo e fixa o tenant antes da primeira consulta (mesmo molde do cancelamento de pedidos abandonados).
/// Nenhum bypass de RLS: o que uma empresa lê e grava é só dela.
/// </para>
///
/// <para>
/// A falha de uma empresa é registrada e não para as outras; o cancelamento do host sobe.
/// </para>
/// </summary>
public static class RodadaPorEmpresa
{
    public static async Task<ResultadoRodadaPorEmpresa> ExecutarAsync(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        string job,
        Func<IServiceProvider, Guid, CancellationToken, Task> porEmpresa,
        CancellationToken ct)
    {
        var empresas = new List<Guid>();
        using (var escopoDaLista = scopeFactory.CreateScope())
        {
            await foreach (var empresa in escopoDaLista.ServiceProvider.GetRequiredService<IEmpresaRepository>().StreamAllAsync(ct))
                empresas.Add(empresa.Id);
        }

        var falhas = 0;
        foreach (var empresaId in empresas)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var escopo = scopeFactory.CreateScope();
                escopo.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresaId);
                await porEmpresa(escopo.ServiceProvider, empresaId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                falhas++;
                logger.LogError(ex, "{Job}: falha na empresa {EmpresaId}; segue com as demais e tenta de novo na próxima rodada.", job, empresaId);
            }
        }

        return new ResultadoRodadaPorEmpresa(empresas.Count, falhas);
    }
}
