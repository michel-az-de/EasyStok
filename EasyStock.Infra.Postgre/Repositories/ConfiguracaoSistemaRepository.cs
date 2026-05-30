using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementação Postgre da persistência de configurações do sistema (F7).
/// </summary>
public sealed class ConfiguracaoSistemaRepository(EasyStockDbContext db) : IConfiguracaoSistemaRepository
{
    public async Task<IReadOnlyList<ConfiguracaoSistemaItem>> ListarTodasAsync(CancellationToken ct = default)
        => await db.ConfiguracoesSistema
            .AsNoTracking()
            .OrderBy(c => c.Chave)
            .Select(c => new ConfiguracaoSistemaItem(c.Chave, c.Valor, c.Descricao, c.AlteradoEm, c.AlteradoPor))
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<string, string>> ObterPublicasAsync(
        IReadOnlyCollection<string> chavesPublicas, CancellationToken ct = default)
    {
        var configs = await db.ConfiguracoesSistema
            .AsNoTracking()
            .Where(c => chavesPublicas.Contains(c.Chave))
            .Select(c => new { c.Chave, c.Valor })
            .ToListAsync(ct);
        return configs.ToDictionary(c => c.Chave, c => c.Valor);
    }

    public async Task GarantirDefaultsAsync(
        IReadOnlyCollection<ConfiguracaoDefault> defaults, CancellationToken ct = default)
    {
        var existentes = await db.ConfiguracoesSistema.Select(c => c.Chave).ToListAsync(ct);
        var faltando = defaults.Where(d => !existentes.Contains(d.Chave)).ToList();
        if (faltando.Count == 0) return;

        foreach (var d in faltando)
            db.ConfiguracoesSistema.Add(ConfiguracaoSistema.Criar(d.Chave, d.Valor, d.Descricao));

        await db.CommitAsync();
    }

    public async Task<int> AplicarPatchAsync(
        IReadOnlyCollection<ConfiguracaoPatchItem> itens, string alteradoPor, CancellationToken ct = default)
    {
        var chaves = itens.Select(i => i.Chave).ToList();
        var existentes = await db.ConfiguracoesSistema.Where(c => chaves.Contains(c.Chave)).ToListAsync(ct);

        foreach (var item in itens)
        {
            var config = existentes.FirstOrDefault(c => c.Chave == item.Chave);
            if (config is not null)
                config.Atualizar(item.Valor, alteradoPor);
            else
                db.ConfiguracoesSistema.Add(ConfiguracaoSistema.Criar(item.Chave, item.Valor, ""));
        }

        await db.CommitAsync();
        return itens.Count;
    }
}
