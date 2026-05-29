namespace EasyStock.Application.UseCases.TicketSuporte
{
    public sealed record ListarMeusTicketsCommand(
        int Page = 1,
        int PageSize = 20,
        string? Status = null,
        string? Categoria = null);

    public sealed record TicketListDTO(
        Guid Id,
        string Titulo,
        string Status,
        string Categoria,
        int MessageCount,
        DateTime CriadoEm,
        DateTime AlteradoEm);

    public sealed class ListarMeusTicketsUseCase(
        IClienteTicketRepository ticketRepo,
        ICurrentUserAccessor currentUser)
    {
        public async Task<(List<TicketListDTO>, int Total)> ExecuteAsync(ListarMeusTicketsCommand cmd)
        {
            var (tickets, total) = await ticketRepo.GetMeusTicketsAsync(
                currentUser.EmpresaId,
                currentUser.UsuarioId,
                cmd.Page,
                cmd.PageSize,
                cmd.Status,
                cmd.Categoria);

            var dtos = tickets.Select(t => new TicketListDTO(
                t.Id,
                t.Titulo,
                t.Status.ToString(),
                t.Categoria.ToString(),
                t.Mensagens?.Count ?? 0,
                t.CriadoEm,
                t.AlteradoEm)).ToList();

            return (dtos, total);
        }
    }
}
