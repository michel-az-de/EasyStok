namespace EasyStock.Domain.Entities
{
    public class UsoIa
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public int Ano { get; set; }
        public int Mes { get; set; }
        public int TotalGeracoes { get; set; }
        public int TotalTokens { get; set; }
        public DateTime AtualizadoEm { get; set; }
    }
}
