using System;

namespace EasyStock.Domain.Entities
{
    public class TicketAnexo
    {
        public Guid Id { get; set; }
        public Guid TicketId { get; set; }
        public Guid? MensagemId { get; set; }
        public string NomeArquivo { get; set; } = null!;
        public string ContentType { get; set; } = null!;
        public long TamanhoBytes { get; set; }
        public string StorageKey { get; set; } = null!;
        public string Url { get; set; } = null!;
        public bool IsPublico { get; set; }
        public Guid EnviadoPorId { get; set; }
        public bool IsAdmin { get; set; }
        public DateTime CriadoEm { get; set; }

        public AdminTicket? Ticket { get; set; }
        public AdminTicketMensagem? Mensagem { get; set; }
        public Usuario? EnviadoPor { get; set; }

        public static TicketAnexo Criar(
            Guid ticketId,
            Guid? mensagemId,
            string nomeArquivo,
            string contentType,
            long tamanhoBytes,
            string storageKey,
            string url,
            bool isPublico,
            Guid enviadoPorId,
            bool isAdmin)
        {
            return new TicketAnexo
            {
                Id = Guid.NewGuid(),
                TicketId = ticketId,
                MensagemId = mensagemId,
                NomeArquivo = nomeArquivo.Trim(),
                ContentType = contentType.Trim(),
                TamanhoBytes = tamanhoBytes,
                StorageKey = storageKey,
                Url = url,
                IsPublico = isPublico,
                EnviadoPorId = enviadoPorId,
                IsAdmin = isAdmin,
                CriadoEm = DateTime.UtcNow
            };
        }
    }
}
