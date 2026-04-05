using System;

namespace EasyStock.Domain.Events
{
    public sealed record EntradaEstoqueRegistrada(Guid EventoId, DateTime OcorridoEm, Guid ItemEstoqueId, Guid ProdutoId, Guid EmpresaId, int Quantidade, string? CodigoLote) : DomainEvent(EventoId, OcorridoEm);
}
