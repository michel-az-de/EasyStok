using System;
using System.Collections.Generic;

namespace EasyStock.Domain.Entities
{
    public class Loja
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public string Nome { get; set; } = null!;
        public string? Descricao { get; set; }
        public string? Documento { get; set; }
        public string? Endereco { get; set; }
        public string? Telefone { get; set; }
        public bool Ativa { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public ICollection<ItemEstoque>? Itens { get; set; }
        public ICollection<Venda>? Vendas { get; set; }

        public static Loja Criar(Guid empresaId, string nome)
        {
            var agora = DateTime.UtcNow;
            return new Loja
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Nome = nome,
                Ativa = true,
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }
    }
}
