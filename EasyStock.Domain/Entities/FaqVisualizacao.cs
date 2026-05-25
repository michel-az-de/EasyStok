namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Registra acesso a um FaqItem para metrica de popularidade.
    /// Nao guarda PII — IpHash e SHA-256 do IP+salt aplicacao.
    /// </summary>
    public class FaqVisualizacao
    {
        public Guid Id { get; set; }
        public Guid ItemId { get; set; }
        public string IpHash { get; set; } = null!;
        public string? Termo { get; set; }
        public string? Origem { get; set; }
        public DateTime CriadoEm { get; set; }

        public FaqItem? Item { get; set; }

        public static FaqVisualizacao Criar(Guid itemId, string ipHash, string? termo = null, string? origem = null)
        {
            if (itemId == Guid.Empty)
                throw new ArgumentException("ItemId obrigatorio.", nameof(itemId));
            if (string.IsNullOrWhiteSpace(ipHash))
                throw new ArgumentException("IpHash obrigatorio.", nameof(ipHash));

            return new FaqVisualizacao
            {
                Id = Guid.NewGuid(),
                ItemId = itemId,
                IpHash = ipHash,
                Termo = string.IsNullOrWhiteSpace(termo) ? null : termo.Trim(),
                Origem = string.IsNullOrWhiteSpace(origem) ? null : origem.Trim(),
                CriadoEm = DateTime.UtcNow
            };
        }
    }
}
