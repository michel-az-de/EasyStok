using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Faq.Admin
{
    public sealed record AtualizarFaqItemCommand(
        Guid ItemId,
        string Titulo,
        string Conteudo,
        string? ConteudoBusca,
        string[]? Tags,
        int Ordem);

    public sealed class AtualizarFaqItemUseCase(IFaqAdminRepository repo, IUnitOfWork uow)
    {
        public async Task ExecuteAsync(AtualizarFaqItemCommand cmd, CancellationToken ct = default)
        {
            if (cmd.ItemId == Guid.Empty)
                throw new UseCaseValidationException("Item invalido.");

            var item = await repo.ObterItemAsync(cmd.ItemId, ct);
            if (item is null)
                throw new UseCaseValidationException("Item nao encontrado.");

            item.Atualizar(cmd.Titulo, cmd.Conteudo, cmd.ConteudoBusca, cmd.Tags, cmd.Ordem);

            await repo.AtualizarItemAsync(item, ct);
            await uow.CommitAsync();
        }
    }
}
