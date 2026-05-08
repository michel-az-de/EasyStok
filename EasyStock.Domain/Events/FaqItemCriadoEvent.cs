namespace EasyStock.Domain.Events
{
    /// <summary>
    /// Disparado quando um novo item de FAQ e criado (status Rascunho).
    /// Consumidores: log de auditoria, indexacao de busca.
    /// </summary>
    public sealed record FaqItemCriadoEvent(
        Guid EventoId,
        DateTime OcorridoEm,
        Guid ItemId,
        Guid CategoriaId,
        string Titulo,
        Guid? AutorId) : DomainEvent(EventoId, OcorridoEm);
}
