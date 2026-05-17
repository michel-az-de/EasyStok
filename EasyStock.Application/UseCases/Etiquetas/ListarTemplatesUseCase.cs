using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Etiquetas;

public sealed record ListarTemplatesQuery(Guid EmpresaId);

public class ListarTemplatesUseCase(IEtiquetaTemplateRepository repo)
{
    public async Task<IReadOnlyList<EtiquetaTemplateListItem>> ExecuteAsync(ListarTemplatesQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);

        var sistema  = await repo.ListSistemaAsync();
        var empresa  = await repo.ListEmpresaAsync(q.EmpresaId);
        var padrao   = await repo.GetDefaultAsync(q.EmpresaId);

        var result = new List<EtiquetaTemplateListItem>();

        foreach (var t in sistema)
        {
            var isDefault = padrao != null
                && padrao.TemplateOrigem == "Sistema"
                && padrao.TemplateId == t.Id;

            result.Add(new EtiquetaTemplateListItem(
                "Sistema", t.Id, t.Nome, t.Descricao, t.LayoutJson, isDefault, t.Ordem,
                null, null, null));
        }

        foreach (var t in empresa)
        {
            var isDefault = padrao != null
                && padrao.TemplateOrigem == "Empresa"
                && padrao.TemplateId == t.Id;

            result.Add(new EtiquetaTemplateListItem(
                "Empresa", t.Id, t.Nome, null, t.LayoutJson, isDefault, null,
                t.BaseSistemaId, t.CriadoEm, t.AlteradoEm));
        }

        return result;
    }
}
