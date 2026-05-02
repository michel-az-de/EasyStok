namespace EasyStock.Domain.Events
{
    public sealed record PedidoFornecedorItemRecebido(
        Guid EventoId,
        DateTime OcorridoEm,
        Guid PedidoFornecedorId,
        Guid ItemId,
        Guid ProdutoId,
        Guid EmpresaId,
        decimal QuantidadeRecebida,
        DateTime DataRecebimento) : DomainEvent(EventoId, OcorridoEm);
}
