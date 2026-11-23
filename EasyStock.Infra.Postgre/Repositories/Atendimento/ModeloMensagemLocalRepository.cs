using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class ModeloMensagemLocalRepository(EasyStockDbContext db) : IModeloMensagemLocalRepository
{
    public Task AddAsync(ModeloMensagemLocal modelo, CancellationToken ct = default) =>
        db.ModelosMensagem.AddAsync(modelo, ct).AsTask();

    public Task<ModeloMensagemLocal?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.ModelosMensagem.FirstOrDefaultAsync(m => m.EmpresaId == empresaId && m.Id == id, ct);

    public async Task<IReadOnlyList<ModeloMensagemLocal>> ListarAsync(Guid empresaId, bool incluirArquivados, CancellationToken ct = default)
    {
        var query = db.ModelosMensagem.AsNoTracking().Where(m => m.EmpresaId == empresaId);
        if (!incluirArquivados) query = query.Where(m => !m.Arquivado);
        return await query.OrderBy(m => m.Nome).ThenBy(m => m.Idioma).ToListAsync(ct);
    }

    public Task<bool> ExisteAsync(Guid empresaId, string nome, string idioma, Guid? excetoId, CancellationToken ct = default) =>
        db.ModelosMensagem.AnyAsync(m => m.EmpresaId == empresaId && m.Nome == nome && m.Idioma == idioma
            && (excetoId == null || m.Id != excetoId), ct);
}
