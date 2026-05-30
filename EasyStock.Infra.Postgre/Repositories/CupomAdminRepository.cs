using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementação Postgre do CRUD de cupons no contexto Admin SaaS (F7).
/// </summary>
public sealed class CupomAdminRepository(EasyStockDbContext db) : ICupomAdminRepository
{
    public async Task<IReadOnlyList<CupomAdminItem>> ListarAsync(CancellationToken ct = default)
        => await db.Cupons
            .AsNoTracking()
            .OrderByDescending(c => c.CriadoEm)
            .Select(c => new CupomAdminItem(
                c.Id, c.Codigo, c.TipoDesconto.ToString(), c.Valor, c.LimiteUsos,
                c.TotalUsos, c.ValidoAte, c.PlanoId, c.Ativo, c.CriadoEm))
            .ToListAsync(ct);

    public Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct = default)
        => db.Cupons.AnyAsync(c => c.Codigo == codigo, ct);

    public async Task<CupomResumo> CriarAsync(NovoCupom dados, CancellationToken ct = default)
    {
        var cupom = Cupom.Criar(dados.Codigo, dados.Tipo, dados.Valor, dados.LimiteUsos, dados.ValidoAte, dados.PlanoId);
        db.Cupons.Add(cupom);
        await db.CommitAsync();
        return new CupomResumo(cupom.Id, cupom.Codigo);
    }

    public async Task<AtualizacaoCupomResultado> AtualizarAsync(Guid id, PatchCupom patch, CancellationToken ct = default)
    {
        var cupom = await db.Cupons.FindAsync([id], ct);
        if (cupom is null)
            return new AtualizacaoCupomResultado(AtualizacaoCupomStatus.NaoEncontrado, null);

        TipoDesconto? tipo = null;
        if (!string.IsNullOrWhiteSpace(patch.TipoDesconto))
        {
            if (!Enum.TryParse<TipoDesconto>(patch.TipoDesconto, out var t))
                return new AtualizacaoCupomResultado(AtualizacaoCupomStatus.TipoInvalido, null);
            tipo = t;
        }

        cupom.Atualizar(patch.Codigo, tipo, patch.Valor, patch.LimiteUsos, patch.ValidoAte, patch.PlanoId);
        await db.CommitAsync();
        return new AtualizacaoCupomResultado(AtualizacaoCupomStatus.Atualizado, new CupomResumo(cupom.Id, cupom.Codigo));
    }

    public async Task<CupomAtivoResultado?> AlternarAtivoAsync(Guid id, CancellationToken ct = default)
    {
        var cupom = await db.Cupons.FindAsync([id], ct);
        if (cupom is null) return null;

        cupom.Toggle();
        await db.CommitAsync();
        return new CupomAtivoResultado(cupom.Id, cupom.Ativo);
    }

    public async Task<ExclusaoCupomResultado> ExcluirAsync(Guid id, CancellationToken ct = default)
    {
        var cupom = await db.Cupons.FindAsync([id], ct);
        if (cupom is null)
            return new ExclusaoCupomResultado(ExclusaoCupomStatus.NaoEncontrado, null);
        if (cupom.TotalUsos > 0)
            return new ExclusaoCupomResultado(ExclusaoCupomStatus.EmUso, cupom.Codigo);

        db.Cupons.Remove(cupom);
        await db.CommitAsync();
        return new ExclusaoCupomResultado(ExclusaoCupomStatus.Excluido, cupom.Codigo);
    }
}
