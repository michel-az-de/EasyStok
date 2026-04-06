using System;

namespace EasyStock.Domain.Events
{
    public sealed record ReposicaoEstoqueRegistrada(Guid EventoId, DateTime OcorridoEm, Guid ItemEstoqueId, Guid ProdutoId, Guid EmpresaId, int Quantidade, string? Fonte) : DomainEvent(EventoId, OcorridoEm);
}
