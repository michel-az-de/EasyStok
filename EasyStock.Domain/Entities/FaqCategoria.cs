
namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Categoria publica do FAQ (sem multi-tenant — base global).
    /// Slug e unico globalmente; Nome e mostrado ao usuario final.
    /// </summary>
    public class FaqCategoria
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string? Descricao { get; set; }
        public string? Icone { get; set; }
        public int Ordem { get; set; }
        public bool Publica { get; set; } = true;
        public DateTime CriadoEm { get; set; }
        public DateTime AtualizadoEm { get; set; }

        public ICollection<FaqItem> Itens { get; set; } = new List<FaqItem>();

        public static FaqCategoria Criar(string nome, string slug, string? descricao = null, string? icone = null, int ordem = 0)
        {
            if (string.IsNullOrWhiteSpace(nome) || nome.Length > 80)
                throw new ArgumentException("Nome da categoria invalido (1-80).", nameof(nome));
            if (string.IsNullOrWhiteSpace(slug) || slug.Length > 80)
                throw new ArgumentException("Slug da categoria invalido (1-80).", nameof(slug));

            var agora = DateTime.UtcNow;
            return new FaqCategoria
            {
                Id = Guid.NewGuid(),
                Nome = nome.Trim(),
                Slug = slug.Trim().ToLowerInvariant(),
                Descricao = descricao?.Trim(),
                Icone = icone?.Trim(),
                Ordem = ordem,
                Publica = true,
                CriadoEm = agora,
                AtualizadoEm = agora
            };
        }

        public void Atualizar(string nome, string? descricao, string? icone, int ordem, bool publica)
        {
            if (string.IsNullOrWhiteSpace(nome) || nome.Length > 80)
                throw new ArgumentException("Nome da categoria invalido (1-80).", nameof(nome));

            Nome = nome.Trim();
            Descricao = descricao?.Trim();
            Icone = icone?.Trim();
            Ordem = ordem;
            Publica = publica;
            AtualizadoEm = DateTime.UtcNow;
        }
    }
}
