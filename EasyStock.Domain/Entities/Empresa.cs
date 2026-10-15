namespace EasyStock.Domain.Entities
{
    public class Empresa
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = null!;
        public string? Documento { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }
        /// <summary>
        /// Marcador de seed — [NotMapped] pois a coluna não existe em produção
        /// (migration ficou vazia). Cleanup usa documento fixo via SeedDocumentos[].
        /// </summary>
        [NotMapped]
        public bool IsSeedData { get; set; }

        /// <summary>
        /// Feature flag do modulo Financeiro (F1+). Default false em tenants
        /// existentes (Casa da Baba nao deve ver menu Financeiro ate ser
        /// explicitamente habilitado). Default true em tenants criados depois
        /// da migration que introduziu a coluna.
        /// </summary>
        public bool FinanceiroHabilitado { get; set; } = false;

        public string? NomeFantasia { get; set; }
        public string? Telefone { get; set; }
        public string? Segmento { get; set; }
        public bool OnboardingCompleto { get; set; } = false;
        public DateTime? OnboardingCompletoEm { get; set; }

        /// <summary>
        /// phone_number_id da Cloud API da Meta vinculado a esta empresa — usado pelo webhook
        /// (S03) para rotear metadata.phone_number_id ate o tenant certo.
        /// </summary>
        public string? WhatsAppPhoneNumberId { get; set; }

        /// <summary>Id da página do Facebook: roteia o webhook do Messenger pelo <c>recipient.id</c> (S35).</summary>
        public string? FacebookPageId { get; set; }

        /// <summary>Id da conta profissional do Instagram: roteia o webhook do Instagram pelo <c>recipient.id</c> (S35).</summary>
        public string? InstagramAccountId { get; set; }

        public static Empresa Criar(string nome, string? documento)
        {
            var agora = DateTime.UtcNow;
            return new Empresa
            {
                Id = Guid.NewGuid(),
                Nome = nome.Trim(),
                Documento = documento?.Trim(),
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }

        public void MarcarOnboardingCompleto()
        {
            if (OnboardingCompleto) return;
            OnboardingCompleto = true;
            OnboardingCompletoEm = DateTime.UtcNow;
            AlteradoEm = DateTime.UtcNow;
        }

        public void VincularWhatsApp(string phoneNumberId)
        {
            WhatsAppPhoneNumberId = phoneNumberId;
            AlteradoEm = DateTime.UtcNow;
        }

        /// <summary>Página do Facebook e conta do Instagram desta empresa (S35). Vazio desvincula.</summary>
        public void VincularMensageriaMeta(string? facebookPageId, string? instagramAccountId)
        {
            FacebookPageId = string.IsNullOrWhiteSpace(facebookPageId) ? null : facebookPageId.Trim();
            InstagramAccountId = string.IsNullOrWhiteSpace(instagramAccountId) ? null : instagramAccountId.Trim();
            AlteradoEm = DateTime.UtcNow;
        }

        /// <summary>Tira o número da Meta da empresa: o webhook deixa de rotear para ela e o envio cai no número global.</summary>
        public void DesvincularWhatsApp()
        {
            WhatsAppPhoneNumberId = null;
            AlteradoEm = DateTime.UtcNow;
        }

        public ICollection<Categoria> Categorias { get; set; } = new List<Categoria>();
        public ICollection<Produto> Produtos { get; set; } = new List<Produto>();
        public ICollection<ProdutoVariacao> VariacoesProduto { get; set; } = new List<ProdutoVariacao>();
        public ICollection<ProdutoCaracteristica> CaracteristicasProduto { get; set; } = new List<ProdutoCaracteristica>();
        public ICollection<ProdutoEmbalagem> EmbalagensProduto { get; set; } = new List<ProdutoEmbalagem>();
        public ICollection<ItemEstoque> ItensEstoque { get; set; } = new List<ItemEstoque>();
        public ICollection<Venda> Vendas { get; set; } = new List<Venda>();
        public ICollection<MovimentacaoEstoque> Movimentacoes { get; set; } = new List<MovimentacaoEstoque>();
        public ICollection<Loja> Lojas { get; set; } = new List<Loja>();
    }
}
