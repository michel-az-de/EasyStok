using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Item do FAQ. Conteudo em Markdown; ConteudoBusca em texto plano para
    /// indice GIN/FTS Postgres. Slug e unico dentro da categoria.
    /// </summary>
    public class FaqItem
    {
        public Guid Id { get; set; }
        public Guid CategoriaId { get; set; }
        public string Titulo { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string Conteudo { get; set; } = null!;
        public string ConteudoBusca { get; set; } = null!;
        public string TagsCsv { get; set; } = string.Empty;
        public FaqStatus Status { get; set; } = FaqStatus.Rascunho;
        public DateTime? PublicadoEm { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public Guid? AutorId { get; set; }
        public int Ordem { get; set; }
        public int Visualizacoes { get; set; }
        public int UtilCount { get; set; }
        public int NaoUtilCount { get; set; }

        public FaqCategoria? Categoria { get; set; }

        public string[] Tags => string.IsNullOrWhiteSpace(TagsCsv)
            ? Array.Empty<string>()
            : TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public static FaqItem Criar(
            Guid categoriaId,
            string titulo,
            string slug,
            string conteudo,
            string? conteudoBusca = null,
            string[]? tags = null,
            Guid? autorId = null,
            int ordem = 0)
        {
            ValidarTitulo(titulo);
            ValidarSlug(slug);
            ValidarConteudo(conteudo);

            var agora = DateTime.UtcNow;
            return new FaqItem
            {
                Id = Guid.NewGuid(),
                CategoriaId = categoriaId,
                Titulo = titulo.Trim(),
                Slug = slug.Trim().ToLowerInvariant(),
                Conteudo = conteudo,
                ConteudoBusca = (conteudoBusca ?? StripMarkdown(conteudo)).Trim(),
                TagsCsv = tags is null ? string.Empty : string.Join(',', tags),
                Status = FaqStatus.Rascunho,
                PublicadoEm = null,
                CriadoEm = agora,
                AtualizadoEm = agora,
                AutorId = autorId,
                Ordem = ordem,
                Visualizacoes = 0,
                UtilCount = 0,
                NaoUtilCount = 0
            };
        }

        public void Atualizar(string titulo, string conteudo, string? conteudoBusca, string[]? tags, int ordem)
        {
            ValidarTitulo(titulo);
            ValidarConteudo(conteudo);

            Titulo = titulo.Trim();
            Conteudo = conteudo;
            ConteudoBusca = (conteudoBusca ?? StripMarkdown(conteudo)).Trim();
            TagsCsv = tags is null ? string.Empty : string.Join(',', tags);
            Ordem = ordem;
            AtualizadoEm = DateTime.UtcNow;
        }

        public void Publicar()
        {
            if (Status == FaqStatus.Publicado) return;
            Status = FaqStatus.Publicado;
            PublicadoEm = DateTime.UtcNow;
            AtualizadoEm = PublicadoEm.Value;
        }

        public void Arquivar()
        {
            if (Status == FaqStatus.Arquivado) return;
            Status = FaqStatus.Arquivado;
            AtualizadoEm = DateTime.UtcNow;
        }

        public void RegistrarVisualizacao() => Visualizacoes++;

        public void RegistrarFeedback(bool util)
        {
            if (util) UtilCount++;
            else NaoUtilCount++;
        }

        private static void ValidarTitulo(string titulo)
        {
            if (string.IsNullOrWhiteSpace(titulo) || titulo.Length > 200)
                throw new ArgumentException("Titulo do FAQ invalido (1-200).", nameof(titulo));
        }

        private static void ValidarSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug) || slug.Length > 200)
                throw new ArgumentException("Slug do FAQ invalido (1-200).", nameof(slug));
        }

        private static void ValidarConteudo(string conteudo)
        {
            if (string.IsNullOrWhiteSpace(conteudo) || conteudo.Length > 20_000)
                throw new ArgumentException("Conteudo do FAQ invalido (1-20000).", nameof(conteudo));
        }

        private static string StripMarkdown(string md)
        {
            // basico: remove # > * _ ` [ ] ( ) — ja serve pra FTS portugues
            var clean = md
                .Replace("#", " ")
                .Replace(">", " ")
                .Replace("*", " ")
                .Replace("_", " ")
                .Replace("`", " ")
                .Replace("[", " ")
                .Replace("]", " ")
                .Replace("(", " ")
                .Replace(")", " ");
            return clean;
        }
    }
}
