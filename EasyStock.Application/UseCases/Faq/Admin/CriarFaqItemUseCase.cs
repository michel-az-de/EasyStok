using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;

namespace EasyStock.Application.UseCases.Faq.Admin
{
    public sealed record CriarFaqItemCommand(
        Guid CategoriaId,
        string Titulo,
        string Slug,
        string Conteudo,
        string? ConteudoBusca,
        string[]? Tags,
        int Ordem,
        Guid? AutorId);

    public sealed record CriarFaqItemResult(Guid ItemId, string Slug);

    public sealed class CriarFaqItemUseCase(IFaqAdminRepository repo, IUnitOfWork uow)
    {
        public async Task<CriarFaqItemResult> ExecuteAsync(CriarFaqItemCommand cmd, CancellationToken ct = default)
        {
            if (cmd.CategoriaId == Guid.Empty)
                throw new UseCaseValidationException("Categoria invalida.");

            var categoria = await repo.ObterCategoriaAsync(cmd.CategoriaId, ct);
            if (categoria is null)
                throw new UseCaseValidationException("Categoria nao encontrada.");

            var slugNormalizado = (cmd.Slug ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(slugNormalizado))
                throw new UseCaseValidationException("Slug obrigatorio.");

            if (await repo.ItemSlugExisteAsync(cmd.CategoriaId, slugNormalizado, null, ct))
                throw new UseCaseValidationException("Ja existe item com esse slug nesta categoria.");

            var item = FaqItem.Criar(
                cmd.CategoriaId,
                cmd.Titulo,
                slugNormalizado,
                cmd.Conteudo,
                cmd.ConteudoBusca,
                cmd.Tags,
                cmd.AutorId,
                cmd.Ordem);

            await repo.InserirItemAsync(item, ct);
            await uow.CommitAsync();

            return new CriarFaqItemResult(item.Id, item.Slug);
        }
    }
}
