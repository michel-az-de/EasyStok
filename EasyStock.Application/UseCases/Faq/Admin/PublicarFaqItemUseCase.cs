namespace EasyStock.Application.UseCases.Faq.Admin
{
    public sealed record PublicarFaqItemCommand(Guid ItemId);

    public sealed class PublicarFaqItemUseCase(IFaqAdminRepository repo, IUnitOfWork uow)
    {
        public async Task ExecuteAsync(PublicarFaqItemCommand cmd, CancellationToken ct = default)
        {
            if (cmd.ItemId == Guid.Empty)
                throw new UseCaseValidationException("Item invalido.");

            var item = await repo.ObterItemAsync(cmd.ItemId, ct);
            if (item is null)
                throw new UseCaseValidationException("Item nao encontrado.");

            item.Publicar();

            await repo.AtualizarItemAsync(item, ct);
            await uow.CommitAsync();
        }
    }

    public sealed record ArquivarFaqItemCommand(Guid ItemId);

    public sealed class ArquivarFaqItemUseCase(IFaqAdminRepository repo, IUnitOfWork uow)
    {
        public async Task ExecuteAsync(ArquivarFaqItemCommand cmd, CancellationToken ct = default)
        {
            if (cmd.ItemId == Guid.Empty)
                throw new UseCaseValidationException("Item invalido.");

            var item = await repo.ObterItemAsync(cmd.ItemId, ct);
            if (item is null)
                throw new UseCaseValidationException("Item nao encontrado.");

            item.Arquivar();

            await repo.AtualizarItemAsync(item, ct);
            await uow.CommitAsync();
        }
    }
}
