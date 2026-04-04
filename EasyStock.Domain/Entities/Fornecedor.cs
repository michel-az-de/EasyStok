using System;

namespace EasyStock.Domain.Entities
{
    public class Fornecedor
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public string Nome { get; set; } = null!;
        public string? Documento { get; set; }
        public string? Email { get; set; }
        public string? Telefone { get; set; }
        public string? Contato { get; set; }
        public bool Ativo { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }

        public static Fornecedor Criar(Guid empresaId, string nome)
        {
            var agora = DateTime.UtcNow;
            return new Fornecedor
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Nome = nome,
                Ativo = true,
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }
    }
}
