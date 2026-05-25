using System;

namespace EasyStock.Domain.Events
{
    public sealed record EstoqueBaixoIdentificado(Guid EventoId, DateTime OcorridoEm, Guid ProdutoId, Guid EmpresaId, int QuantidadeAtual, int Limite) : DomainEvent(EventoId, OcorridoEm);
}
