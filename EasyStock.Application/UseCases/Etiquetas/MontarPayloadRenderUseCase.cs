using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Etiquetas;

public sealed record MontarPayloadRenderQuery(
    Guid EmpresaId,
    Guid LoteId,
    string? TemplateOrigem,   // null → usa default da empresa
    Guid? TemplateId);

public class MontarPayloadRenderUseCase(
    IEtiquetaTemplateRepository templateRepo,
    ILoteRepository loteRepo,
    ILojaRepository lojaRepo)
{
    public async Task<EtiquetaRenderPayload?> ExecuteAsync(MontarPayloadRenderQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);

        var etiquetas = (await loteRepo.GetEtiquetasForRenderAsync(q.EmpresaId, q.LoteId)).ToList();
        if (etiquetas.Count == 0) return null;

        var layoutJson = await ResolveLayoutJsonAsync(q);

        // Loja para logoUrl e nome da empresa
        var lojas = (await lojaRepo.GetByEmpresaAsync(q.EmpresaId)).ToList();
        var loja = lojas.FirstOrDefault(l => l.Ativa) ?? lojas.FirstOrDefault();
        var empresa = new EmpresaRenderDto(
            loja?.Nome ?? "EasyStok",
            loja?.LogoUrl);

        var produtosSemFicha = new List<Guid>();
        var items = new List<EtiquetaRenderItem>();

        foreach (var e in etiquetas)
        {
            var produto = e.LoteItem?.Produto;
            if (produto == null) continue;

            var (ficha, _) = ProdutoFichaTecnica.TryParse(produto.AtributosJson);
            if (ficha == null) produtosSemFicha.Add(produto.Id);

            var produtoDto = new ProdutoRenderDto(
                produto.Id,
                produto.Nome,
                produto.Marca,
                e.LoteItem?.Emoji,
                e.LoteItem?.Unidade,
                e.LoteItem?.PesoG, // C2 (RDC 727/2022): peso por unidade vai pra etiqueta.
                ficha?.Kcal,
                ficha?.ProteinaG,
                ficha?.CarbsG,
                ficha?.GorduraG,
                ficha?.GorduraSaturadaG,
                ficha?.FibrasG,
                ficha?.SodioMg,
                ficha?.PorcaoG,
                ficha?.ModoPreparo,
                ficha?.Alergenos ?? [],
                ficha != null);

            items.Add(new EtiquetaRenderItem(
                e.Id, e.Sequencial, e.Codigo, e.Status,
                produtoDto,
                e.Lote?.Codigo, e.LoteItem?.ExpiraEm, e.Lote?.DataProducao ?? DateTime.UtcNow,
                e.LayoutSnapshotJson, e.LayoutSnapshotMeta));
        }

        return new EtiquetaRenderPayload(layoutJson, empresa, items, produtosSemFicha);
    }

    private async Task<string> ResolveLayoutJsonAsync(MontarPayloadRenderQuery q)
    {
        if (q.TemplateOrigem != null && q.TemplateId.HasValue)
        {
            if (q.TemplateOrigem == "Sistema")
            {
                var t = await templateRepo.GetSistemaByIdAsync(q.TemplateId.Value);
                if (t != null) return t.LayoutJson;
            }
            else
            {
                var t = await templateRepo.GetEmpresaByIdAsync(q.EmpresaId, q.TemplateId.Value);
                if (t != null) return t.LayoutJson;
            }
        }

        // Usa default da empresa
        var padrao = await templateRepo.GetDefaultAsync(q.EmpresaId);
        if (padrao != null)
        {
            if (padrao.TemplateOrigem == "Sistema")
            {
                var t = await templateRepo.GetSistemaByIdAsync(padrao.TemplateId);
                if (t != null) return t.LayoutJson;
            }
            else
            {
                var t = await templateRepo.GetEmpresaByIdAsync(q.EmpresaId, padrao.TemplateId);
                if (t != null) return t.LayoutJson;
            }
        }

        // Fallback: primeiro sistema (Identificação, Ordem=0)
        var sistema = await templateRepo.ListSistemaAsync();
        var fallback = sistema.OrderBy(s => s.Ordem).FirstOrDefault();
        return fallback?.LayoutJson ?? "{}";
    }
}
