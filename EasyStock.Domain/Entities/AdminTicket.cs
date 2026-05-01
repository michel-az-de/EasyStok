using System;
using System.Collections.Generic;
using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities
{
    public class AdminTicket
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public string Titulo { get; set; } = null!;
        public string Descricao { get; set; } = null!;
        public TicketStatus Status { get; set; }
        public TicketCategoria Categoria { get; set; }
        public TicketPrioridade Prioridade { get; set; }
        public Guid? CriadoPorId { get; set; }
        public Guid? AtendenteId { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Usuario? CriadoPor { get; set; }
        public Usuario? Atendente { get; set; }
        public ICollection<AdminTicketMensagem> Mensagens { get; set; } = new List<AdminTicketMensagem>();

        public static AdminTicket Criar(Guid empresaId, string titulo, string descricao, TicketCategoria categoria, TicketPrioridade prioridade)
        {
            var agora = DateTime.UtcNow;
            return new AdminTicket
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Titulo = titulo.Trim(),
                Descricao = descricao.Trim(),
                Status = TicketStatus.Aberto,
                Categoria = categoria,
                Prioridade = prioridade,
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }
    }
}
