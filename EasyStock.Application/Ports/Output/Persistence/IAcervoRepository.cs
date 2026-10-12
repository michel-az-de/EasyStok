using EasyStock.Domain.Entities.Acervo;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IAcervoRepository
{
    Task<IReadOnlyList<AcervoImagem>> ListarAsync(Guid empresaId, int pagina, CancellationToken ct);
    Task<IReadOnlyList<AcervoImagem>> BuscarAsync(Guid empresaId, int pagina, string busca, string origem, string formato, string natureza, Guid? itemId, string canal, CancellationToken ct);
    Task<IReadOnlyList<AcervoImagem>> MapaImagensAsync(Guid empresaId, CancellationToken ct);
    Task<IReadOnlyList<AcervoSolicitacao>> MapaSolicitacoesAsync(Guid empresaId, CancellationToken ct);
    Task<IReadOnlyList<AcervoBriefing>> MapaBriefingsAsync(Guid empresaId, CancellationToken ct);
    Task<IReadOnlyList<AcervoSolicitacao>> HistoricoFilaAsync(Guid empresaId, CancellationToken ct);
    Task<IReadOnlyList<CardapioItem>> ItensAsync(Guid empresaId, CancellationToken ct);
    Task<IReadOnlyList<AcervoUso>> UsosAsync(Guid empresaId, CancellationToken ct);
    Task<AcervoUso?> UsoAsync(Guid empresaId, Guid imagemId, string canal, CancellationToken ct);
    Task<IReadOnlyList<AcervoBriefing>> BriefingsAsync(Guid empresaId, CancellationToken ct);
    Task<AcervoBriefing?> BriefingAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task<AcervoBriefing?> BriefingPorHashAsync(Guid empresaId, string hash, CancellationToken ct);
    Task<int> UltimaVersaoAsync(Guid empresaId, Guid raizId, CancellationToken ct);
    Task<AcervoSolicitacao?> PendenteBriefingAsync(Guid empresaId, Guid briefingId, CancellationToken ct);
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
    void Adicionar(AcervoBriefing briefing);
    void Adicionar(AcervoUso uso);
}
