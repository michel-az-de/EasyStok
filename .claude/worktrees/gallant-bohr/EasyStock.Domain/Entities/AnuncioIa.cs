namespace EasyStock.Domain.Entities
{
    public class AnuncioIa
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ProdutoId { get; set; }
        public Guid? ProdutoVariacaoId { get; set; }
        public string Titulo { get; set; } = null!;
        public string Conteudo { get; set; } = null!;
        public string? InstrucoesUsadas { get; set; }
        public int TokensConsumidos { get; set; }
        public bool Salvo { get; set; }
        public DateTime CriadoEm { get; set; }

        public Produto? Produto { get; set; }
    }
}
