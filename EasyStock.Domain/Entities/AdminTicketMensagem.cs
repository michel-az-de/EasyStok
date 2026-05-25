using System;

namespace EasyStock.Domain.Entities
{
    public class AdminTicketMensagem
    {
        public Guid Id { get; set; }
        public Guid TicketId { get; set; }
        public Guid AutorId { get; set; }
        public string Conteudo { get; set; } = null!;
        public bool IsAdmin { get; set; }
        public bool LidoPeloAdmin { get; set; }
        public bool Interno { get; set; }
        public DateTime CriadoEm { get; set; }

        public AdminTicket? Ticket { get; set; }
        public Usuario? Autor { get; set; }

        public static AdminTicketMensagem Criar(Guid ticketId, Guid autorId, string conteudo, bool isAdmin, bool interno = false)
        {
            return new AdminTicketMensagem
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                AutorId = autorId,
                Conteudo = conteudo.Trim(),
                IsAdmin = isAdmin,
                Interno = interno,
                CriadoEm = DateTime.UtcNow
            };
        }
    }
}
