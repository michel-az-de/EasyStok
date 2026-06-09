using System.Text.Json;
using EasyStock.Application.Ports.Output.Helpdesk;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.UseCases.TicketSuporte
{
    /// <summary>Command de abertura de ticket pelo cliente do tenant.</summary>
    /// <param name="Titulo">Titulo curto do ticket.</param>
    /// <param name="Descricao">Descricao detalhada (vai como primeira mensagem).</param>
    /// <param name="Categoria">Categoria do helpdesk.</param>
    /// <param name="FaturaId">
    /// FK opcional a uma <see cref="Fatura"/> que motivou o ticket.
    /// Quando informado, valida pertencimento a empresa do user e
    /// vincula a fatura ao ticket bidirecionalmente (F9).
    /// </param>
    public sealed record AbrirTicketClienteCommand(
        string Titulo,
        string Descricao,
        TicketCategoria Categoria,
        /// <summary>
        /// FK opcional a uma <see cref="Fatura"/> que motivou o ticket.
        /// Quando informado, valida pertencimento a empresa do user e
        /// vincula a fatura ao ticket bidirecionalmente (F9).
        /// </summary>
        Guid? FaturaId = null,
        /// <summary>
        /// FK opcional a um <see cref="Pedido"/> que motivou o ticket.
        /// Quando informado, valida pertencimento a empresa do user,
        /// vincula o pedido ao ticket e registra PedidoEvento "ticket_aberto"
        /// na trilha de auditoria do pedido (Onda 1.1).
        /// </summary>
        Guid? PedidoId = null,
        CanalOrigem CanalOrigem = CanalOrigem.Pwa);

    public sealed record AbrirTicketClienteResult(
        Guid TicketId,
        string Status,
        DateTime CriadoEm);

    public sealed class AbrirTicketClienteUseCase(
        IClienteTicketRepository ticketRepo,
        IFaturaRepository faturaRepo,
        IPedidoRepository pedidoRepo,
        ISlaResolver slaResolver,
        INotificadorService notificador,
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUser)
    {
        public async Task<AbrirTicketClienteResult> ExecuteAsync(
            AbrirTicketClienteCommand cmd,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cmd.Titulo) || cmd.Titulo.Length > 200)
                throw new UseCaseValidationException("Título inválido");

            if (string.IsNullOrWhiteSpace(cmd.Descricao) || cmd.Descricao.Length > 5000)
                throw new UseCaseValidationException("Descrição inválida");

            // F9 — Valida que a fatura pertence a empresa do user e captura
            // referencia para vinculacao bidirecional. 404 amigavel se nao existe.
            Fatura? fatura = null;
            if (cmd.FaturaId.HasValue && cmd.FaturaId.Value != Guid.Empty)
            {
                fatura = await faturaRepo.GetByIdAsync(currentUser.EmpresaId, cmd.FaturaId.Value, ct);
                if (fatura is null)
                    throw new UseCaseValidationException("Fatura não encontrada ou não pertence à sua empresa.");
            }

            // Onda 1.1 — Valida que o pedido pertence a empresa do user.
            // Espelha guard do FaturaId. Defesa em camadas: HasQueryFilter global
            // ja filtra por tenant, mas o use case valida explicitamente para
            // dar mensagem 400 amigavel em vez de NotFound silencioso.
            EasyStock.Domain.Entities.Pedido? pedido = null;
            if (cmd.PedidoId.HasValue && cmd.PedidoId.Value != Guid.Empty)
            {
                pedido = await pedidoRepo.GetByIdAsync(currentUser.EmpresaId, cmd.PedidoId.Value);
                if (pedido is null)
                    throw new UseCaseValidationException("Pedido não encontrado ou não pertence à sua empresa.");
            }

            // Prioridade default Normal — cliente nao escolhe (so admin via PATCH).
            var prioridade = TicketPrioridade.Normal;
            var sla = await slaResolver.ResolverAsync(currentUser.EmpresaId, prioridade, ct: ct);

            var ticket = AdminTicket.Criar(
                empresaId: currentUser.EmpresaId,
                titulo: cmd.Titulo,
                descricao: cmd.Descricao,
                categoria: cmd.Categoria,
                prioridade: prioridade,
                prazoResposta: sla.PrazoResposta,
                prazoResolucao: sla.PrazoResolucao,
                criadoPorId: currentUser.UsuarioId,
                canalOrigem: cmd.CanalOrigem);
            ticket.FaturaId = fatura?.Id;
            ticket.PedidoId = pedido?.Id;

            ticket.Mensagens.Add(AdminTicketMensagem.Criar(
                ticketId: ticket.Id,
                autorId: currentUser.UsuarioId,
                conteudo: cmd.Descricao,
                isAdmin: false));

            await ticketRepo.InsertAsync(ticket);
            await ticketRepo.AddHistoricoAsync(TicketHistorico.Criar(
                ticketId: ticket.Id,
                autorId: currentUser.UsuarioId,
                acao: TicketAcaoHistorico.Criado,
                metadadosJson: JsonSerializer.Serialize(new
                {
                    prioridade = ticket.Prioridade.ToString(),
                    nivel = ticket.Nivel.ToString(),
                    categoria = ticket.Categoria.ToString(),
                    canalOrigem = ticket.CanalOrigem.ToString(),
                    faturaId = fatura?.Id,
                    pedidoId = pedido?.Id
                })));

            // Vinculacao reversa: Fatura.TicketRelacionadoId aponta para o
            // primeiro ticket sobre ela (idempotente — se ja vinculada, mantem).
            if (fatura is not null && fatura.VinculaTicket(ticket.Id))
            {
                await faturaRepo.UpdateAsync(fatura);
            }

            // Trilha cruzada: PedidoEvento "ticket_aberto" liga o pedido ao
            // ticket que o motivou. Apenas registro; nao altera estado do pedido.
            if (pedido is not null)
            {
                await pedidoRepo.AddEventoAsync(new PedidoEvento
                {
                    Id = Guid.NewGuid(),
                    PedidoId = pedido.Id,
                    Tipo = "ticket_aberto",
                    Detalhes = JsonSerializer.Serialize(new { ticketId = ticket.Id, titulo = ticket.Titulo }),
                    UsuarioId = currentUser.UsuarioId,
                    Origem = "api",
                    OcorridoEm = DateTime.UtcNow
                });
            }

            // ADR-0030: enfileira o evento no MESMO commit do negocio (atomico, nada aguardado
            // pos-commit — se a notificacao falhasse pos-commit, antes corrompia a resposta do
            // cliente). Sem modelo de destinatario de fila ainda (P1-C) o avaliador marca
            // Falhado; a wiring fica pronta para o P1-C resolver o destino do time de atendimento.
            await notificador.EnfileirarEventoAsync(
                TipoEventoNotificacao.TicketCriado,
                currentUser.EmpresaId,
                payloadJson: JsonSerializer.Serialize(new
                {
                    ticketId = ticket.Id,
                    titulo = ticket.Titulo,
                    prioridade = ticket.Prioridade.ToString(),
                    categoria = ticket.Categoria.ToString(),
                    canalOrigem = ticket.CanalOrigem.ToString(),
                    faturaId = fatura?.Id,
                    pedidoId = pedido?.Id,
                    abertoPorCliente = true
                }),
                refEntidadeId: ticket.Id,
                ct: ct);

            await unitOfWork.CommitAsync();

            return new(ticket.Id, ticket.Status.ToString(), ticket.CriadoEm);
        }
    }
}
