using System;

namespace EasyStock.Domain.Events
{
    public sealed record ItemEstoqueVencidoIdentificado(Guid EventoId, DateTime OcorridoEm, Guid ItemEstoqueId, Guid ProdutoId, Guid EmpresaId, DateTime DataValidade) : DomainEvent(EventoId, OcorridoEm);
}
