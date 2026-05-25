using System;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Audit de alterações de <see cref="Venda"/>. Onda P4 — skeleton.
    ///
    /// Hoje a Venda é gerada via pipeline de pedido (Onda 3 do mobile)
    /// e tem caminhos de mutação limitados (ex: cancelamento). A entity
    /// fica registrada já no padrão pra que ondas futuras (estorno
    /// formal, ajuste de NF, recálculo) tenham onde gravar diff.
    /// </summary>
    public class VendaAlteracao
    {
        public Guid Id { get; set; }
        public Guid VendaId { get; set; }
        public Guid? AlteradoPorUserId { get; set; }
        public string? AlteradoPorNome { get; set; }
        public string Campo { get; set; } = null!;
        public string? ValorAntigo { get; set; }
        public string? ValorNovo { get; set; }
        public DateTime AlteradoEm { get; set; }
        public string? Origem { get; set; } // "web" | "mobile" | "api"

        public Venda? Venda { get; set; }
    }
}
