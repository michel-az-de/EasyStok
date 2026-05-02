namespace EasyStock.Domain.Events
{
    public sealed record PedidoFornecedorRecebido(
        Guid EventoId,
        DateTime OcorridoEm,
        Guid PedidoId,
        Guid EmpresaId,
        Guid FornecedorId,
        int TotalItensRecebidos,
        DateTime DataRecebimento) : DomainEvent(EventoId, OcorridoEm);
}
