using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Ticket de suporte criado pelo operador ou automaticamente (ex: falha de pagamento).
    /// Ciclo de vida: Aberto → Atribuido → EmAndamento → Resolvido/Fechado/Cancelado.
    /// Suporta escalada entre niveis (N1/N2/N3) e rastreamento de SLA de resposta e resolucao.
    /// </summary>
    public class AdminTicket
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public string Titulo { get; set; } = null!;
        public string Descricao { get; set; } = null!;
        public TicketStatus Status { get; set; }
        public TicketCategoria Categoria { get; set; }
        public TicketPrioridade Prioridade { get; set; }
        public NivelAtendimento Nivel { get; set; } = NivelAtendimento.N1;
        public CanalOrigem CanalOrigem { get; set; } = CanalOrigem.Admin;
        public Guid? CriadoPorId { get; set; }
        public Guid? AtendenteId { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        // SLA
        public DateTime? PrazoResposta { get; set; }
        public DateTime? PrazoResolucao { get; set; }
        public DateTime? PrimeiraRespostaEm { get; set; }
        public DateTime? ResolvidoEm { get; set; }
        public bool SlaRespostaViolado { get; set; }
        public bool SlaResolucaoViolado { get; set; }
        public DateTime? UltimoAlerta50PctEm { get; set; }
        public DateTime? UltimoAlerta80PctEm { get; set; }

        // Auto-relacionamento (bug-fix encaminhado para dev)
        public Guid? OrigemTicketId { get; set; }

        /// <summary>
        /// FK opcional a uma <see cref="Fatura"/> relacionada (categoria=Financeiro).
        /// Permite cliente abrir ticket sobre uma fatura especifica e admin ver
        /// fatura linkada no detalhe. ON DELETE SET NULL.
        /// </summary>
        public Guid? FaturaId { get; set; }

        public Empresa? Empresa { get; set; }
        public Usuario? CriadoPor { get; set; }
        public Usuario? Atendente { get; set; }
        public AdminTicket? OrigemTicket { get; set; }
        public AdminTicketTecnicoMeta? MetaTecnico { get; set; }
        public Fatura? Fatura { get; set; }
        public ICollection<AdminTicketMensagem> Mensagens { get; set; } = new List<AdminTicketMensagem>();

        public static AdminTicket Criar(
            Guid empresaId,
            string titulo,
            string descricao,
            TicketCategoria categoria,
            TicketPrioridade prioridade,
            NivelAtendimento nivel = NivelAtendimento.N1,
            DateTime? prazoResposta = null,
            DateTime? prazoResolucao = null,
            Guid? origemTicketId = null,
            Guid? criadoPorId = null,
            CanalOrigem canalOrigem = CanalOrigem.Admin)
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
                Nivel = nivel,
                CanalOrigem = canalOrigem,
                PrazoResposta = prazoResposta,
                PrazoResolucao = prazoResolucao,
                OrigemTicketId = origemTicketId,
                CriadoPorId = criadoPorId,
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }
    }
}
