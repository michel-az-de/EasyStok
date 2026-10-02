using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

/// <summary>Estado dos templates da Meta (N6). Tabela global: não depende de tenant corrente.</summary>
public sealed class TemplateMetaEstadoRepository(EasyStockDbContext db) : ITemplateMetaEstadoRepository
{
    public Task<TemplateMetaEstado?> ObterAsync(string nome, string idioma, CancellationToken ct = default)
    {
        var n = TemplateMetaEstado.NormalizarNome(nome);
        var i = TemplateMetaEstado.NormalizarIdioma(idioma);
        return db.NotifTemplatesMetaEstado.AsNoTracking().FirstOrDefaultAsync(x => x.Nome == n && x.Idioma == i, ct);
    }

    public async Task GravarCategoriaAsync(string nome, string idioma, string categoria, CancellationToken ct = default)
    {
        var n = TemplateMetaEstado.NormalizarNome(nome);
        var i = TemplateMetaEstado.NormalizarIdioma(idioma);
        var existente = await db.NotifTemplatesMetaEstado.FirstOrDefaultAsync(x => x.Nome == n && x.Idioma == i, ct);
        if (existente is null)
            db.NotifTemplatesMetaEstado.Add(TemplateMetaEstado.Criar(nome, idioma, categoria));
        else
            existente.AtualizarCategoria(categoria);
    }
}
