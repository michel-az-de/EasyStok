using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// #3063 (S2a de #2948): cada registro usa um escopo e um DbContext próprios, com o tenant da chamada fixado, para não
/// salvar junto as alterações pendentes de quem chamou o LLM. Singleton: só depende da fábrica de escopos.
/// </summary>
public sealed class RegistroChamadaIaRepository(IServiceScopeFactory escopos) : IRegistroChamadaIa
{
    public async Task RegistrarAsync(ChamadaIa chamada, CancellationToken ct = default)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(chamada.EmpresaId);
        db.ChamadasIa.Add(chamada);
        await db.SaveChangesAsync(ct);
    }

    public async Task<UsoMensalIa> SomarMesAsync(Guid empresaId, DateTime agoraUtc, CancellationToken ct = default)
    {
        // #3295 (R6): o mês é o de Brasília, o mesmo da cota de tokens (PortoesDoAssistente.UsadosNoMesAsync).
        var (inicio, fim) = HorarioBrasil.JanelaMesUtc(HorarioBrasil.DataOperacional(agoraUtc));
        await using var escopo = escopos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(empresaId);
        var soma = await db.ChamadasIa.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.CriadoEm >= inicio && c.CriadoEm < fim)
            .GroupBy(_ => 1)
            .Select(g => new UsoMensalIa(g.Count(), g.Sum(c => (long)c.TokensEntrada), g.Sum(c => (long)c.TokensSaida),
                g.Sum(c => c.CustoUsd ?? 0m)))
            .SingleOrDefaultAsync(ct);
        return soma ?? new UsoMensalIa(0, 0, 0, 0m);
    }
}
