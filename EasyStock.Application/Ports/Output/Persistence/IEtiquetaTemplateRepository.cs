namespace EasyStock.Application.Ports.Output.Persistence;

public interface IEtiquetaTemplateRepository
{
    Task<IEnumerable<EtiquetaTemplateSistema>> ListSistemaAsync();
    Task<EtiquetaTemplateSistema?> GetSistemaByIdAsync(Guid id);

    Task<IEnumerable<EtiquetaTemplate>> ListEmpresaAsync(Guid empresaId);
    Task<EtiquetaTemplate?> GetEmpresaByIdAsync(Guid empresaId, Guid id);
    Task AddEmpresaAsync(EtiquetaTemplate template);
    Task UpdateEmpresaAsync(EtiquetaTemplate template);
    Task RemoveEmpresaAsync(EtiquetaTemplate template);

    Task<EtiquetaEmpresaDefault?> GetDefaultAsync(Guid empresaId);
    Task UpsertDefaultAsync(EtiquetaEmpresaDefault defaultEntry);

    Task<int> CountSnapshotsByTemplateIdAsync(Guid templateId);
}
