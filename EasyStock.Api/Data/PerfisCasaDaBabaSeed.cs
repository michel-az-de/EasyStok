using EasyStock.Domain.Services;
using System.Text.Json;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data;

public static class PerfisCasaDaBabaSeed
{
    // Só a empresa configurada. Não cria usuários nem troca vínculos ou perfis personalizados.
    public static async Task ExecutarAsync(EasyStockDbContext db, Guid empresaId, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty || !await db.Empresas.AnyAsync(e => e.Id == empresaId, ct))
            throw new InvalidOperationException("Empresa dos perfis Casa da Baba não encontrada.");

        // Arquivo e remoção ativa são salvos juntos, somente no tenant resolvido para a Casa da Baba.
        var perfis = await db.Perfis.IgnoreQueryFilters().Where(p => p.EmpresaId == empresaId)
            .Include(p => p.Permissoes).ToListAsync(ct);
        foreach (var perfil in perfis)
        {
            var legadas = perfil.Permissoes.Where(p => PermissoesLegadas.Valores.Contains(p.Permissao)).ToArray();
            if (legadas.Length == 0) continue;
            perfil.PermissoesExplicitas = true;
            var arquivo = db.Entry(perfil).Property<string?>("PermissoesLegadas");
            var historico = arquivo.CurrentValue is null ? []
                : JsonSerializer.Deserialize<List<PermissaoArquivada>>(arquivo.CurrentValue)!;
            historico.AddRange(legadas.Select(p => new PermissaoArquivada(p.Id, p.PerfilId, p.Permissao.ToString())));
            arquivo.CurrentValue = JsonSerializer.Serialize(historico);
            db.RemoveRange(legadas);
        }

        // Startup não tem JWT. O bypass de RLS não desliga o filtro global do EF.
        var existentes = await db.Perfis.IgnoreQueryFilters().Where(p => p.EmpresaId == empresaId)
            .Select(p => p.Nome).ToListAsync(ct);
        foreach (var modelo in PerfisCasaDaBaba.Iniciais)
        {
            if (existentes.Contains(modelo.Nome, StringComparer.OrdinalIgnoreCase)) continue;
            var perfil = new Perfil
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = modelo.Nome,
                Nivel = modelo.Nivel, ModuloInicial = modelo.ModuloInicial, PermissoesExplicitas = true,
                CriadoEm = DateTime.UtcNow
            };
            perfil.Permissoes = modelo.Permissoes.Select(p => new PerfilPermissao
            {
                Id = Guid.NewGuid(), PerfilId = perfil.Id, Permissao = p
            }).ToList();
            db.Perfis.Add(perfil);
        }
        await db.SaveChangesAsync(ct);
    }

    private sealed record PermissaoArquivada(Guid Id, Guid PerfilId, string Permissao);
}
