using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Helpdesk;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.UseCases.TicketSuporte
{
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
        CanalOrigem CanalOrigem = CanalOrigem.Pwa);

    public sealed record AbrirTicketClienteResult(
        Guid TicketId,
        string Status,
        DateTime CriadoEm);

    public sealed class AbrirTicketClienteUseCase(
        IClienteTicketRepository ticketRepo,
        IFaturaRepository faturaRepo,
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
                    faturaId = fatura?.Id
                })));

            // Vinculacao reversa: Fatura.TicketRelacionadoId aponta para o
            // primeiro ticket sobre ela (idempotente — se ja vinculada, mantem).
            if (fatura is not null && fatura.VinculaTicket(ticket.Id))
            {
                await faturaRepo.UpdateAsync(fatura);
            }

            await unitOfWork.CommitAsync();

            // Notifica admins/atendentes — evento outbox publicado fora da transacao
            // do ticket de proposito: se a notificacao falhar, ticket permanece criado.
            await notificador.PublicarEventoAsync(
                TipoEventoNotificacao.TicketCriado,
                currentUser.EmpresaId,
                usuarioDestinoId: null,
                payloadJson: JsonSerializer.Serialize(new
                {
                    ticketId = ticket.Id,
                    titulo = ticket.Titulo,
                    prioridade = ticket.Prioridade.ToString(),
                    categoria = ticket.Categoria.ToString(),
                    canalOrigem = ticket.CanalOrigem.ToString(),
                    abertoPorCliente = true
                }),
                ct: ct);

            return new(ticket.Id, ticket.Status.ToString(), ticket.CriadoEm);
        }
    }
}
