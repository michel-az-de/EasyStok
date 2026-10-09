using EasyStock.Domain.Services;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Data;

public static class PerfisCasaDaBabaSeed
{
    // Só a empresa configurada. Não cria usuários nem troca vínculos ou perfis personalizados.
    public static async Task ExecutarAsync(EasyStockDbContext db, Guid empresaId, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty || !await db.Empresas.AnyAsync(e => e.Id == empresaId, ct))
            throw new InvalidOperationException("Empresa dos perfis Casa da Baba não encontrada.");

        // Startup não tem JWT. O bypass de RLS não desliga o filtro global do EF.
        var existentes = await db.Perfis.IgnoreQueryFilters().Where(p => p.EmpresaId == empresaId)
            .Select(p => p.Nome).ToListAsync(ct);
        foreach (var modelo in PerfisCasaDaBaba.Iniciais)
        {
            if (existentes.Contains(modelo.Nome, StringComparer.OrdinalIgnoreCase)) continue;
            var perfil = new Perfil
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = modelo.Nome,
                Nivel = modelo.Nivel, ModuloInicial = modelo.ModuloInicial, CriadoEm = DateTime.UtcNow
            };
            perfil.Permissoes = modelo.Permissoes.Select(p => new PerfilPermissao
            {
                Id = Guid.NewGuid(), PerfilId = perfil.Id, Permissao = p
            }).ToList();
            db.Perfis.Add(perfil);
        }
        await db.SaveChangesAsync(ct);
    }
}
