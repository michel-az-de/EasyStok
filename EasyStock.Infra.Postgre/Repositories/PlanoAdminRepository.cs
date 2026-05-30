using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementação Postgre do CRUD de planos no contexto Admin SaaS (F7).
/// </summary>
public sealed class PlanoAdminRepository(EasyStockDbContext db) : IPlanoAdminRepository
{
    public async Task<IReadOnlyList<PlanoAdminItem>> ListarComTenantsAsync(CancellationToken ct = default)
        => await db.Planos
            .AsNoTracking()
            .OrderBy(p => p.PrecoMensal)
            .Select(p => new PlanoAdminItem(
                p.Id, p.Nome, p.Descricao, p.LimiteLojas, p.LimiteUsuarios, p.LimiteProdutos,
                p.LimiteGeracoesIaMensais, p.PrecoMensal, p.Ativo, p.CriadoEm,
                db.AssinaturasEmpresa.Count(a => a.PlanoId == p.Id && a.Status == StatusAssinatura.Ativa)))
            .ToListAsync(ct);

    public async Task<PlanoResumo> CriarAsync(NovoPlano dados, CancellationToken ct = default)
    {
        var plano = new Plano
        {
            Id = Guid.NewGuid(),
            Nome = dados.Nome,
            Descricao = dados.Descricao,
            LimiteLojas = dados.LimiteLojas,
            LimiteUsuarios = dados.LimiteUsuarios,
            LimiteProdutos = dados.LimiteProdutos,
            LimiteGeracoesIaMensais = dados.LimiteGeracoesIaMensais,
            PrecoMensal = dados.PrecoMensal,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        db.Planos.Add(plano);
        await db.CommitAsync();
        return new PlanoResumo(plano.Id, plano.Nome);
    }

    public async Task<PlanoResumo?> AtualizarAsync(Guid id, PatchPlano patch, CancellationToken ct = default)
    {
        var plano = await db.Planos.FindAsync([id], ct);
        if (plano is null) return null;

        if (patch.Nome is not null) plano.Nome = patch.Nome;
        if (patch.Descricao is not null) plano.Descricao = patch.Descricao;
        if (patch.LimiteLojas.HasValue) plano.LimiteLojas = patch.LimiteLojas.Value;
        if (patch.LimiteUsuarios.HasValue) plano.LimiteUsuarios = patch.LimiteUsuarios.Value;
        if (patch.LimiteProdutos.HasValue) plano.LimiteProdutos = patch.LimiteProdutos.Value;
        if (patch.LimiteGeracoesIaMensais.HasValue) plano.LimiteGeracoesIaMensais = patch.LimiteGeracoesIaMensais.Value;
        if (patch.PrecoMensal.HasValue) plano.PrecoMensal = patch.PrecoMensal.Value;

        await db.CommitAsync();
        return new PlanoResumo(plano.Id, plano.Nome);
    }

    public async Task<PlanoAtivoResultado?> AlternarAtivoAsync(Guid id, CancellationToken ct = default)
    {
        var plano = await db.Planos.FindAsync([id], ct);
        if (plano is null) return null;

        plano.Ativo = !plano.Ativo;
        await db.CommitAsync();
        return new PlanoAtivoResultado(plano.Id, plano.Ativo);
    }
}
