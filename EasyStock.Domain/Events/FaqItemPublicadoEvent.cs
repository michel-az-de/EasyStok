namespace EasyStock.Domain.Events
{
    /// <summary>
    /// Disparado quando um item de FAQ passa de Rascunho para Publicado.
    /// Consumidores: cache invalidation, sitemap, indexacao full-text.
    /// </summary>
    public sealed record FaqItemPublicadoEvent(
        Guid EventoId,
        DateTime OcorridoEm,
        Guid ItemId,
        Guid CategoriaId,
        string Titulo,
        string Slug) : DomainEvent(EventoId, OcorridoEm);
}
