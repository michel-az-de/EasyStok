using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.Lotes;

namespace EasyStock.Application.UseCases.ObterLoteDetalhes;

public sealed record ObterLoteDetalhesQuery(Guid EmpresaId, Guid Id);

public class ObterLoteDetalhesUseCase(ILoteRepository repo)
{
    public async Task<LoteDetalheResult?> ExecuteAsync(ObterLoteDetalhesQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(q.Id, "Id");

        var l = await repo.GetByIdWithDetailsAsync(q.EmpresaId, q.Id);
        if (l == null) return null;

        return new LoteDetalheResult(
            CriarLoteUseCase.Map(l),
            l.Itens.OrderBy(i => i.CriadoEm).Select(i => new LoteItemResult(
                i.Id, i.LoteId, i.ProdutoId, i.Nome, i.Emoji, i.Unidade,
                i.Quantidade, i.PesoG, i.ValidadeDias, i.ExpiraEm,
                i.FotoUrl, i.CriadoEm)).ToList(),
            l.Etiquetas.OrderBy(e => e.Sequencial).Select(e => new LoteEtiquetaResult(
                e.Id, e.LoteId, e.LoteItemId, e.Sequencial, e.Codigo, e.Status,
                e.ConferidaEm, e.ConferidaPorUserId, e.ConferidaPorNome,
                e.ObservacaoConferencia, e.CriadoEm,
                e.LayoutSnapshotJson, e.LayoutSnapshotMeta)).ToList()
        );
    }
}
