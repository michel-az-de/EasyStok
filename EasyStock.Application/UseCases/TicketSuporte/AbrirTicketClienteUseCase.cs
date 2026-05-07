using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

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
        Guid? FaturaId = null);

    public sealed record AbrirTicketClienteResult(
        Guid TicketId,
        string Status,
        DateTime CriadoEm);

    public sealed class AbrirTicketClienteUseCase(
        IClienteTicketRepository ticketRepo,
        IFaturaRepository faturaRepo,
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUser)
    {
        public async Task<AbrirTicketClienteResult> ExecuteAsync(AbrirTicketClienteCommand cmd)
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
                fatura = await faturaRepo.GetByIdAsync(currentUser.EmpresaId, cmd.FaturaId.Value);
                if (fatura is null)
                    throw new UseCaseValidationException("Fatura não encontrada ou não pertence à sua empresa.");
            }

            var ticket = new AdminTicket
            {
                Id = Guid.NewGuid(),
                EmpresaId = currentUser.EmpresaId,
                Titulo = cmd.Titulo,
                Status = TicketStatus.Aberto,
                Prioridade = TicketPrioridade.Normal,
                Categoria = cmd.Categoria,
                CriadoPorId = currentUser.UsuarioId,
                FaturaId = fatura?.Id,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            };

            ticket.Mensagens.Add(new AdminTicketMensagem
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Conteudo = cmd.Descricao,
                AutorId = currentUser.UsuarioId,
                IsAdmin = false,
                CriadoEm = DateTime.UtcNow
            });

            await ticketRepo.InsertAsync(ticket);

            // Vinculacao reversa: Fatura.TicketRelacionadoId aponta para o
            // primeiro ticket sobre ela (idempotente — se ja vinculada, mantem).
            if (fatura is not null && fatura.VinculaTicket(ticket.Id))
            {
                await faturaRepo.UpdateAsync(fatura);
            }

            await unitOfWork.CommitAsync();

            return new(ticket.Id, ticket.Status.ToString(), ticket.CriadoEm);
        }
    }
}
