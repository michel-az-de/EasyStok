using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Marcas dos passos da configuração inicial (#2070). O upsert <c>ON CONFLICT ("EmpresaId", "Passo")</c> é atômico: dois
/// aparelhos marcando passos diferentes gravam os dois, e o mesmo passo fica numa linha só (vale a última marca). Roda
/// na conexão do contexto, sob o RLS do tenant; <c>EmpresaId</c> também vai no WHERE da leitura (ADR-0010).
/// </summary>
public sealed class ConfiguracaoInicialRepository(EasyStockDbContext db) : IConfiguracaoInicialRepository
{
    public async Task<IReadOnlyList<PassoMarcado>> ListarPassosAsync(Guid empresaId, CancellationToken ct = default) =>
        await (from p in db.ConfiguracaoInicialPassos.AsNoTracking()
               where p.EmpresaId == empresaId
               join u in db.Usuarios.AsNoTracking() on p.PorUsuarioId equals u.Id into autores
               from u in autores.DefaultIfEmpty()
               orderby p.Passo
               select new PassoMarcado(p.Passo, p.Situacao, p.Em, p.PorUsuarioId, u == null ? null : u.Nome))
            .ToListAsync(ct);

    public Task MarcarAsync(PassoConfiguracaoInicial passo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(passo);
        var situacao = SituacoesDoPasso.Texto(passo.Situacao);
        return db.Database.ExecuteSqlAsync($"""
            INSERT INTO configuracao_inicial_passos ("EmpresaId", "Passo", "Situacao", "Em", "PorUsuarioId")
            VALUES ({passo.EmpresaId}, {passo.Passo}, {situacao}, {passo.Em}, {passo.PorUsuarioId})
            ON CONFLICT ("EmpresaId", "Passo") DO UPDATE
            SET "Situacao" = EXCLUDED."Situacao", "Em" = EXCLUDED."Em", "PorUsuarioId" = EXCLUDED."PorUsuarioId"
            """, ct);
    }
}
