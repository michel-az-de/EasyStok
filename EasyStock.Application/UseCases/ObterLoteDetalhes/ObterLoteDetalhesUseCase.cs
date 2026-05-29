using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.Lotes;

namespace EasyStock.Application.UseCases.ObterLoteDetalhes;

public sealed record ObterLoteDetalhesQuery(Guid EmpresaId, Guid Id);

public class ObterLoteDetalhesUseCase(ILoteRepository repo, IProdutoRepository produtoRepo)
{
    public async Task<LoteDetalheResult?> ExecuteAsync(ObterLoteDetalhesQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(q.Id, "Id");

        var l = await repo.GetByIdWithDetailsAsync(q.EmpresaId, q.Id);
        if (l == null) return null;

        // C2: lookup batch do TipoEmbalagem dos produtos vinculados. Itens sem
        // ProdutoId vinculado (legado pre-R1) ficam como "Avulso" default.
        var produtoIds = l.Itens
            .Where(i => i.ProdutoId.HasValue)
            .Select(i => i.ProdutoId!.Value)
            .Distinct()
            .ToList();
        var tipoMap = produtoIds.Count > 0
            ? await produtoRepo.GetTipoEmbalagemMapAsync(q.EmpresaId, produtoIds)
            : new Dictionary<Guid, TipoEmbalagem>();

        return new LoteDetalheResult(
            CriarLoteUseCase.Map(l),
            l.Itens.OrderBy(i => i.CriadoEm).Select(i =>
            {
                var tipoEmb = "Avulso";
                if (i.ProdutoId.HasValue && tipoMap.TryGetValue(i.ProdutoId.Value, out var t))
                    tipoEmb = t.ToString();
                return new LoteItemResult(
                    i.Id, i.LoteId, i.ProdutoId, i.Nome, i.Emoji, i.Unidade,
                    i.Quantidade, i.PesoG, i.ValidadeDias, i.ExpiraEm,
                    i.FotoUrl, i.CriadoEm, tipoEmb);
            }).ToList(),
            l.Etiquetas.OrderBy(e => e.Sequencial).Select(e => new LoteEtiquetaResult(
                e.Id, e.LoteId, e.LoteItemId, e.Sequencial, e.Codigo, e.Status,
                e.ConferidaEm, e.ConferidaPorUserId, e.ConferidaPorNome,
                e.ObservacaoConferencia, e.CriadoEm,
                e.LayoutSnapshotJson, e.LayoutSnapshotMeta)).ToList()
        );
    }
}
