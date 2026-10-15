using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Acervo;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class AcervoRepository(EasyStockDbContext db) : IAcervoRepository
{
    public async Task<IReadOnlyList<AcervoImagem>> BuscarAsync(Guid empresaId, int pagina, string busca, string origem, string formato, string natureza, Guid? itemId, string canal, CancellationToken ct)
    {
        var q = db.Set<AcervoImagem>().Where(x => x.EmpresaId == empresaId);
        if (busca.Length > 0) q = q.Where(x => x.Titulo.ToLower().Contains(busca.ToLower()));
        if (origem.Length > 0) q = q.Where(x => x.Origem == origem);
        if (formato.Length > 0) q = q.Where(x => x.Formato == formato);
        if (natureza.Length > 0) q = q.Where(x => x.Natureza == natureza);
        if (itemId is { } item) { var vinculo = System.Text.Json.JsonSerializer.Serialize(new[] { item }); q = q.Where(x => EF.Functions.JsonContains(x.ItensJson, vinculo)); }
        if (canal.Length > 0) q = q.Where(x => db.Set<AcervoUso>().Any(u => u.EmpresaId == empresaId && u.ImagemId == x.Id && u.Canal == canal));
        return await q.OrderByDescending(x => x.CriadoEm).ThenBy(x => x.Id).Skip(pagina * 60).Take(60).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<AcervoImagem>> MapaImagensAsync(Guid empresaId, CancellationToken ct) =>
        await db.Set<AcervoImagem>().AsNoTracking().Where(x => x.EmpresaId == empresaId).Select(x => new AcervoImagem {
            Id = x.Id, Titulo = x.Titulo, ItensJson = x.ItensJson, Origem = x.Origem, Natureza = x.Natureza, Formato = x.Formato,
            Largura = x.Largura, Altura = x.Altura, AprovadoEm = x.AprovadoEm }).ToListAsync(ct);
    public async Task<IReadOnlyList<CardapioItem>> ItensAsync(Guid empresaId, CancellationToken ct) =>
        await db.CardapioItens.AsNoTracking().Include(x => x.Produto).Where(x => db.Storefronts.Any(sf => sf.Id == x.StorefrontId && sf.EmpresaId == empresaId)).OrderBy(x => x.Id).ToListAsync(ct);
    public async Task<IReadOnlyList<AcervoUso>> UsosAsync(Guid empresaId, CancellationToken ct) =>
        await db.Set<AcervoUso>().AsNoTracking().Where(x => x.EmpresaId == empresaId).ToListAsync(ct);
    public async Task<IReadOnlyList<AcervoSolicitacao>> MapaSolicitacoesAsync(Guid empresaId, CancellationToken ct) =>
        await db.Set<AcervoSolicitacao>().AsNoTracking().Where(x => x.EmpresaId == empresaId && x.ConcluidoEm == null)
            .Select(x => new AcervoSolicitacao { ItemId = x.ItemId, BriefingId = x.BriefingId }).ToListAsync(ct);
    public async Task<IReadOnlyList<AcervoBriefing>> MapaBriefingsAsync(Guid empresaId, CancellationToken ct) =>
        await db.Set<AcervoBriefing>().AsNoTracking().Where(x => x.EmpresaId == empresaId)
            .Select(x => new AcervoBriefing { Id = x.Id, Canal = x.Canal }).ToListAsync(ct);
    public async Task<IReadOnlyList<AcervoSolicitacao>> HistoricoFilaAsync(Guid empresaId, CancellationToken ct)
    {
        var q = db.Set<AcervoSolicitacao>().AsNoTracking().Where(x => x.EmpresaId == empresaId);
        var concluidas = q.Where(x => x.ConcluidoEm != null).OrderByDescending(x => x.CriadoEm).ThenBy(x => x.Id).Take(200).Select(x => x.Id);
        return await q.Where(x => x.ConcluidoEm == null || concluidas.Contains(x.Id)).OrderByDescending(x => x.CriadoEm).ThenBy(x => x.Id).ToListAsync(ct);
    }
    public Task<AcervoUso?> UsoAsync(Guid empresaId, Guid imagemId, string canal, CancellationToken ct) =>
        db.Set<AcervoUso>().FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.ImagemId == imagemId && x.Canal == canal, ct);
    public async Task<IReadOnlyList<AcervoBriefing>> BriefingsAsync(Guid empresaId, CancellationToken ct) =>
        await db.Set<AcervoBriefing>().AsNoTracking().Where(x => x.EmpresaId == empresaId).OrderByDescending(x => x.CriadoEm).ThenBy(x => x.Id).Take(200).ToListAsync(ct);
    public Task<AcervoBriefing?> BriefingAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.Set<AcervoBriefing>().FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.Id == id, ct);
    public Task<AcervoBriefing?> BriefingPorHashAsync(Guid empresaId, string hash, CancellationToken ct) =>
        db.Set<AcervoBriefing>().FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.ConteudoHash == hash, ct);
    public async Task<int> UltimaVersaoAsync(Guid empresaId, Guid raizId, CancellationToken ct) =>
        await db.Set<AcervoBriefing>().Where(x => x.EmpresaId == empresaId && x.RaizId == raizId).MaxAsync(x => (int?)x.Versao, ct) ?? 0;
    public Task<AcervoSolicitacao?> PendenteBriefingAsync(Guid empresaId, Guid briefingId, CancellationToken ct) =>
        db.Set<AcervoSolicitacao>().FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.BriefingId == briefingId, ct);
    public async Task<IReadOnlyList<AcervoImagem>> ListarAsync(Guid empresaId, int pagina, CancellationToken ct) =>
        await db.Set<AcervoImagem>().Where(x => x.EmpresaId == empresaId).OrderByDescending(x => x.CriadoEm).ThenBy(x => x.Id)
            .Skip(pagina * 60).Take(60).ToListAsync(ct);
    public Task<AcervoImagem?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.Set<AcervoImagem>().FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.Id == id, ct);
    public Task<AcervoImagem?> PorHashAsync(Guid empresaId, string hash, CancellationToken ct) =>
        db.Set<AcervoImagem>().FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.Sha256 == hash, ct);
    public Task<CardapioItem?> ItemAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.CardapioItens.FirstOrDefaultAsync(x => x.Id == id && db.Storefronts.Any(sf => sf.Id == x.StorefrontId && sf.EmpresaId == empresaId), ct);
    public async Task BloquearItemAsync(Guid empresaId, Guid id, CancellationToken ct)
    {
        // Serializa publicações deste item, inclusive a retomada da mesma operação.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({empresaId.ToString() + id.ToString()}, 0))", ct);
    }
    public async Task BloquearImagemAsync(Guid empresaId, Guid id, CancellationToken ct)
    {
        // #1808: publicar em prato novo grava o vínculo na imagem; duas publicações da mesma imagem em
        // pratos diferentes não podem perder um vínculo. Sempre depois do bloqueio do item (mesma ordem).
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"imagem:" + empresaId.ToString() + id.ToString()}, 0))", ct);
    }
    public Task<AcervoPublicacao?> PublicacaoAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.Set<AcervoPublicacao>().FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.Id == id, ct);
    public async Task BloquearSolicitacaoAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({empresaId.ToString() + ":solicitacao:" + id.ToString()}, 0))", ct);
    public async Task<IReadOnlyList<AcervoPublicacao>> HistoricoAsync(Guid empresaId, Guid imagemId, CancellationToken ct) =>
        await db.Set<AcervoPublicacao>().Where(x => x.EmpresaId == empresaId && x.ImagemId == imagemId).OrderByDescending(x => x.CriadoEm).Take(100).ToListAsync(ct);
    public async Task<IReadOnlyList<AcervoSolicitacao>> SolicitacoesAsync(Guid empresaId, CancellationToken ct) =>
        await db.Set<AcervoSolicitacao>().Where(x => x.EmpresaId == empresaId && x.ConcluidoEm == null).OrderBy(x => x.CriadoEm).Take(100).ToListAsync(ct);
    public Task<AcervoSolicitacao?> SolicitacaoAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.Set<AcervoSolicitacao>().FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.Id == id, ct);
    public void Adicionar(AcervoImagem imagem) => db.Add(imagem);
    public void Adicionar(AcervoPublicacao publicacao) => db.Add(publicacao);
    public void Adicionar(AcervoSolicitacao solicitacao) => db.Add(solicitacao);
    public void Adicionar(AcervoBriefing briefing) => db.Add(briefing);
    public void Adicionar(AcervoUso uso) => db.Add(uso);
}
