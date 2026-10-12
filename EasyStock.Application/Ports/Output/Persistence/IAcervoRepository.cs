using EasyStock.Domain.Entities.Acervo;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IAcervoRepository
{
    Task<IReadOnlyList<AcervoImagem>> ListarAsync(Guid empresaId, int pagina, CancellationToken ct);
    Task<AcervoImagem?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task<AcervoImagem?> PorHashAsync(Guid empresaId, string hash, CancellationToken ct);
    Task<CardapioItem?> ItemAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task BloquearItemAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task BloquearSolicitacaoAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task<AcervoPublicacao?> PublicacaoAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<AcervoPublicacao>> HistoricoAsync(Guid empresaId, Guid imagemId, CancellationToken ct);
    Task<IReadOnlyList<AcervoSolicitacao>> SolicitacoesAsync(Guid empresaId, CancellationToken ct);
    Task<AcervoSolicitacao?> SolicitacaoAsync(Guid empresaId, Guid id, CancellationToken ct);
    void Adicionar(AcervoImagem imagem);
    void Adicionar(AcervoPublicacao publicacao);
    void Adicionar(AcervoSolicitacao solicitacao);
}
