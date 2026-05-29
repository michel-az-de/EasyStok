namespace EasyStock.Domain.Entities
{
    public class AdminTicketTecnicoMeta
    {
        public Guid TicketId { get; set; }
        public string SeveridadeTecnica { get; set; } = "Media";
        public string? ComponenteAfetado { get; set; }
        public string? StackTrace { get; set; }
        public string? FixVersion { get; set; }
        public DateTime? ResolvidoEm { get; set; }
        public DateTime CriadoEm { get; set; }

        public AdminTicket? Ticket { get; set; }

        public static AdminTicketTecnicoMeta Criar(
            Guid ticketId,
            string severidadeTecnica,
            string? componenteAfetado,
            string? stackTrace)
        {
            return new AdminTicketTecnicoMeta
            {
                TicketId = ticketId,
                SeveridadeTecnica = string.IsNullOrWhiteSpace(severidadeTecnica) ? "Media" : severidadeTecnica.Trim(),
                ComponenteAfetado = componenteAfetado?.Trim(),
                StackTrace = stackTrace,
                CriadoEm = DateTime.UtcNow
            };
        }
    }
}
