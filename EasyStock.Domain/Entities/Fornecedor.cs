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
        public string? Categoria { get; set; }
        public string? Tipo { get; set; }
        public int? LeadTimeEstimadoDias { get; set; }
        public decimal? LeadTimeRealMedioDias { get; set; }
        public string? SiteUrl { get; set; }
        public string? PedidoMinimo { get; set; }
        public string? FretePadrao { get; set; }
        public string? Observacoes { get; set; }
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

        public void AtualizarCadastro(
            string nome,
            string? documento,
            string? email,
            string? telefone,
            string? contato,
            string? categoria,
            string? tipo,
            int? leadTimeEstimadoDias,
            string? siteUrl,
            string? pedidoMinimo,
            string? fretePadrao,
            string? observacoes)
        {
            Nome = nome.Trim();
            Documento = Normalizar(documento);
            Email = Normalizar(email);
            Telefone = Normalizar(telefone);
            Contato = Normalizar(contato);
            Categoria = Normalizar(categoria);
            Tipo = Normalizar(tipo);
            LeadTimeEstimadoDias = leadTimeEstimadoDias;
            SiteUrl = Normalizar(siteUrl);
            PedidoMinimo = Normalizar(pedidoMinimo);
            FretePadrao = Normalizar(fretePadrao);
            Observacoes = Normalizar(observacoes);
            AlteradoEm = DateTime.UtcNow;
        }

        public void Desativar()
        {
            Ativo = false;
            AlteradoEm = DateTime.UtcNow;
        }

        public void AtualizarLeadTimeReal(decimal? leadTimeRealMedioDias)
        {
            LeadTimeRealMedioDias = leadTimeRealMedioDias;
            AlteradoEm = DateTime.UtcNow;
        }

        private static string? Normalizar(string? valor) =>
            string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }
}
