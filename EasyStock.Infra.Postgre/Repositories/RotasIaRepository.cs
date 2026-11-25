using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// #3064 (S2b de #2948): rota e gasto da loja, cada leitura num escopo próprio com o tenant da própria loja (RLS).
/// Singleton: só depende da fábrica de escopos, como o <see cref="RegistroChamadaIaRepository"/>.
/// </summary>
public sealed class RotasIaRepository(IServiceScopeFactory escopos) : IRotasIa
{
    public async Task<RotaIa?> ObterAsync(Guid empresaId, string? modeloPedido, CancellationToken ct = default)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(empresaId);

        var ligados = db.ModelosIa.AsNoTracking().Where(m => m.EmpresaId == empresaId && m.Ligado);
        var pedido = modeloPedido?.Trim();
        var modelo = string.IsNullOrEmpty(pedido)
            ? null
            : await ligados.Where(m => m.IdExterno == pedido).OrderByDescending(m => m.Padrao).FirstOrDefaultAsync(ct);
        var caiuNoPadrao = modelo is null;
        modelo ??= await ligados.FirstOrDefaultAsync(m => m.Padrao, ct);
        if (modelo is null) return null;

        var provedor = await db.ProvedoresIa.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EmpresaId == empresaId && p.Id == modelo.ProvedorId && p.Ligado, ct);
        return provedor is null
            ? null
            : new RotaIa(provedor.Id, provedor.BaseUrl, provedor.Autenticacao, provedor.OrcamentoMensalUsd, modelo,
                caiuNoPadrao && !string.IsNullOrEmpty(pedido));
    }

    public async Task<decimal> GastoDoMesAsync(Guid empresaId, Guid provedorId, DateTime agoraUtc, CancellationToken ct = default)
    {
        // #3295 (R6): o mês é o de Brasília, o mesmo da cota de tokens (PortoesDoAssistente.UsadosNoMesAsync).
        var (inicio, fim) = HorarioBrasil.JanelaMesUtc(HorarioBrasil.DataOperacional(agoraUtc));
        await using var escopo = escopos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(empresaId);
        // Índice (empresa_id, provedor_id, criado_em) INCLUDE (custo_usd): soma sem ler a tabela.
        return await db.ChamadasIa.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.ProvedorId == provedorId && c.CriadoEm >= inicio && c.CriadoEm < fim)
            .SumAsync(c => c.CustoUsd ?? 0m, ct);
    }
}
