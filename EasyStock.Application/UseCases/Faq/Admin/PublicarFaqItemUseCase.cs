using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Faq.Admin
{
    public sealed record PublicarFaqItemCommand(Guid ItemId);

    /// <summary>
    /// Publica um item de FAQ (Rascunho ou Arquivado → Publicado).
    /// Apos publicacao o item fica visivel no endpoint publico /api/faq.
    /// Transicoes invalidas lancam excecao no dominio (<see cref="FaqItem.Publicar"/>).
    /// </summary>
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

    /// <summary>
    /// Arquiva um item de FAQ publicado (Publicado → Arquivado).
    /// O item sai do endpoint publico imediatamente apos commit.
    /// </summary>
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
