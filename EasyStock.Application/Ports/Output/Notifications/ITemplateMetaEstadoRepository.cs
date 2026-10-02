using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

/// <summary>
/// Estado dos templates da Meta na WABA (N6). Tabela global: legível sem tenant, como o webhook e o provider de
/// plataforma exigem.
/// </summary>
public interface ITemplateMetaEstadoRepository
{
    /// <summary>O estado do template, sem rastreio, ou <c>null</c> se a Meta nunca avisou. Nome e idioma casam normalizados.</summary>
    Task<TemplateMetaEstado?> ObterAsync(string nome, string idioma, CancellationToken ct = default);

    /// <summary>Grava a categoria atual (insere ou atualiza). Quem chama faz o commit.</summary>
    Task GravarCategoriaAsync(string nome, string idioma, string categoria, CancellationToken ct = default);
}
