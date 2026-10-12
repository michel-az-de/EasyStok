using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Acervo;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class AcervoRepository(EasyStockDbContext db) : IAcervoRepository
{
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
    public Task<AcervoPublicacao?> PublicacaoAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.Set<AcervoPublicacao>().FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.Id == id, ct);
    public async Task<IReadOnlyList<AcervoPublicacao>> HistoricoAsync(Guid empresaId, Guid imagemId, CancellationToken ct) =>
        await db.Set<AcervoPublicacao>().Where(x => x.EmpresaId == empresaId && x.ImagemId == imagemId).OrderByDescending(x => x.CriadoEm).Take(100).ToListAsync(ct);
    public async Task<IReadOnlyList<AcervoSolicitacao>> SolicitacoesAsync(Guid empresaId, CancellationToken ct) =>
        await db.Set<AcervoSolicitacao>().Where(x => x.EmpresaId == empresaId && x.ConcluidoEm == null).OrderBy(x => x.CriadoEm).Take(100).ToListAsync(ct);
    public Task<AcervoSolicitacao?> SolicitacaoAsync(Guid empresaId, Guid id, CancellationToken ct) =>
        db.Set<AcervoSolicitacao>().FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.Id == id, ct);
    public void Adicionar(AcervoImagem imagem) => db.Add(imagem);
    public void Adicionar(AcervoPublicacao publicacao) => db.Add(publicacao);
    public void Adicionar(AcervoSolicitacao solicitacao) => db.Add(solicitacao);
}
