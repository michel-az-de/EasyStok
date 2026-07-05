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

        // CSAT — avaliacao do cliente apos fechamento.
        // NotaCsat 1..5; preenchido via endpoint POST /api/helpdesk/tickets/{id}/avaliacao.
        public int? NotaCsat { get; set; }
        public string? ComentarioCsat { get; set; }
        public DateTime? AvaliadoEm { get; set; }
        // Carimbo do convite enviado (idempotencia: nao enviar segundo convite no mesmo ticket).
        public DateTime? ConviteCsatEnviadoEm { get; set; }

        // Auto-relacionamento (bug-fix encaminhado para dev)
        public Guid? OrigemTicketId { get; set; }

        /// <summary>
        /// FK opcional a uma <see cref="Fatura"/> relacionada (categoria=Financeiro).
        /// Permite cliente abrir ticket sobre uma fatura especifica e admin ver
        /// fatura linkada no detalhe. ON DELETE SET NULL.
        /// </summary>
        public Guid? FaturaId { get; set; }

        /// <summary>
        /// FK opcional a um <see cref="Pedido"/> relacionado. Permite a empresa-cliente
        /// abrir ticket reportando problema/duvida sobre um pedido especifico
        /// (ex: pedido travado, sync falhou, item errado). Admin SaaS ve o pedido
        /// linkado no detalhe (read-only). ON DELETE SET NULL.
        /// </summary>
        public Guid? PedidoId { get; set; }

        public Empresa? Empresa { get; set; }
        public Usuario? CriadoPor { get; set; }
        public Usuario? Atendente { get; set; }
        public AdminTicket? OrigemTicket { get; set; }
        public AdminTicketTecnicoMeta? MetaTecnico { get; set; }
        public Fatura? Fatura { get; set; }
        public Pedido? Pedido { get; set; }
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
            // BUG-1 (#839): invariante de dominio contra estouro de coluna (varchar 200/4000).
            // Blinda callers que nao passam pelo controller (ex.: AutoTicket de falha de pagamento).
            titulo = (titulo ?? string.Empty).Trim();
            descricao = (descricao ?? string.Empty).Trim();
            if (titulo.Length is < 3 or > 200)
                throw new ArgumentException("Título deve ter entre 3 e 200 caracteres.", nameof(titulo));
            if (descricao.Length is < 5 or > 4000)
                throw new ArgumentException("Descrição deve ter entre 5 e 4000 caracteres.", nameof(descricao));

            var agora = DateTime.UtcNow;
            return new AdminTicket
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                Titulo = titulo,
                Descricao = descricao,
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
