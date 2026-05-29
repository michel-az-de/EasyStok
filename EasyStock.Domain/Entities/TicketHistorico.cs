namespace EasyStock.Domain.Entities
{
    public class TicketHistorico
    {
        public Guid Id { get; set; }
        public Guid TicketId { get; set; }
        public Guid? AutorId { get; set; }
        public TicketAcaoHistorico Acao { get; set; }
        public string? ValorAntes { get; set; }
        public string? ValorDepois { get; set; }
        public string? MetadadosJson { get; set; }
        public DateTime CriadoEm { get; set; }

        public AdminTicket? Ticket { get; set; }
        public Usuario? Autor { get; set; }

        public static TicketHistorico Criar(
            Guid ticketId,
            Guid? autorId,
            TicketAcaoHistorico acao,
            string? valorAntes = null,
            string? valorDepois = null,
            string? metadadosJson = null)
        {
            return new TicketHistorico
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                AutorId = autorId,
                Acao = acao,
                ValorAntes = valorAntes,
                ValorDepois = valorDepois,
                MetadadosJson = metadadosJson,
                CriadoEm = DateTime.UtcNow
            };
        }
    }
}
