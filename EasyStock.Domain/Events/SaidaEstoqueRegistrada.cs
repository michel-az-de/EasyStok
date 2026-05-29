namespace EasyStock.Domain.Events
{
    public sealed record SaidaEstoqueRegistrada(Guid EventoId, DateTime OcorridoEm, Guid ItemEstoqueId, Guid ProdutoId, Guid EmpresaId, decimal Quantidade, string? Motivo) : DomainEvent(EventoId, OcorridoEm);
}
