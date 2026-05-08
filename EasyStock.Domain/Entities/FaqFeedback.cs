namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Feedback util/nao util sobre um FaqItem.
    /// </summary>
    public class FaqFeedback
    {
        public Guid Id { get; set; }
        public Guid ItemId { get; set; }
        public bool Util { get; set; }
        public string? Comentario { get; set; }
        public string IpHash { get; set; } = null!;
        public DateTime CriadoEm { get; set; }

        public FaqItem? Item { get; set; }

        public static FaqFeedback Criar(Guid itemId, bool util, string ipHash, string? comentario = null)
        {
            if (itemId == Guid.Empty)
                throw new ArgumentException("ItemId obrigatorio.", nameof(itemId));
            if (string.IsNullOrWhiteSpace(ipHash))
                throw new ArgumentException("IpHash obrigatorio.", nameof(ipHash));
            if (comentario is not null && comentario.Length > 1000)
                throw new ArgumentException("Comentario excede 1000 caracteres.", nameof(comentario));

            return new FaqFeedback
            {
                Id = Guid.NewGuid(),
                ItemId = itemId,
                Util = util,
                Comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim(),
                IpHash = ipHash,
                CriadoEm = DateTime.UtcNow
            };
        }
    }
}
