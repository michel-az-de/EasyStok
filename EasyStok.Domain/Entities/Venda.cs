using System;
using System.Collections.Generic;

namespace EasyStok.Domain.Entities
{
 public class Venda
 {
 public Guid Id { get; set; }
 public Guid EmpresaId { get; set; }
 public string Canal { get; set; } = null!; // LOJA_PROPRIA, MERCADO_LIVRE...
 public string Natureza { get; set; } = null!; // VENDA, PERDA, PREJUIZO
 public DateTime DataVenda { get; set; }
 public DateTime? DataEnvio { get; set; }
 public string? NumeroNotaFiscal { get; set; }
 public decimal ValorTotal { get; set; }
 public string? Observacoes { get; set; }
 public DateTime CriadoEm { get; set; }

 public Empresa? Empresa { get; set; }
 public ICollection<ItemVenda>? ItensVenda { get; set; }
 public ICollection<MovimentacaoEstoque>? Movimentacoes { get; set; }
 }
}
