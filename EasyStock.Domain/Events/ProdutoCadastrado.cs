using System;

namespace EasyStock.Domain.Events
{
    public sealed record ProdutoCadastrado(Guid EventoId, DateTime OcorridoEm, Guid ProdutoId, Guid EmpresaId, string Nome) : DomainEvent(EventoId, OcorridoEm);
}
