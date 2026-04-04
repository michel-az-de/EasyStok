using System;

namespace EasyStock.Domain.Events
{
    public sealed record SaidaEstoqueRegistrada(Guid EventoId, DateTime OcorridoEm, Guid ItemEstoqueId, Guid ProdutoId, Guid EmpresaId, int Quantidade, string? Motivo) : DomainEvent(EventoId, OcorridoEm);
}
