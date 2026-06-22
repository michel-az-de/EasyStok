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
        public string? LogoUrl { get; set; }
        public bool Ativa { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public ICollection<ItemEstoque> Itens { get; set; } = new List<ItemEstoque>();
        public ICollection<Venda> Vendas { get; set; } = new List<Venda>();

        public static Loja Criar(Guid empresaId, string nome)
        {
            var agora = DateTime.UtcNow;
            return new Loja
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Nome = (nome ?? string.Empty).Trim(), // consistencia com Cliente.Criar/Produto (sem espaco nas pontas)
                Ativa = true,
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }

        /// <summary>Soft-delete. Mantém histórico de vendas/itens; loja não aparece em listagens ativas.</summary>
        public void Desativar()
        {
            if (!Ativa) return;
            Ativa = false;
            AlteradoEm = DateTime.UtcNow;
        }

        public void Reativar()
        {
            if (Ativa) return;
            Ativa = true;
            AlteradoEm = DateTime.UtcNow;
        }
    }
}
