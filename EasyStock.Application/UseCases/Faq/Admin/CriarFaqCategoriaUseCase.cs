using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;

namespace EasyStock.Application.UseCases.Faq.Admin
{
    public sealed record CriarFaqCategoriaCommand(
        string Nome,
        string Slug,
        string? Descricao,
        string? Icone,
        int Ordem);

    public sealed record CriarFaqCategoriaResult(Guid CategoriaId, string Slug);

    public sealed class CriarFaqCategoriaUseCase(IFaqAdminRepository repo, IUnitOfWork uow)
    {
        public async Task<CriarFaqCategoriaResult> ExecuteAsync(CriarFaqCategoriaCommand cmd, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cmd.Nome) || cmd.Nome.Length > 80)
                throw new UseCaseValidationException("Nome invalido (1-80).");
            if (string.IsNullOrWhiteSpace(cmd.Slug) || cmd.Slug.Length > 80)
                throw new UseCaseValidationException("Slug invalido (1-80).");

            var slugNormalizado = cmd.Slug.Trim().ToLowerInvariant();
            if (await repo.CategoriaSlugExisteAsync(slugNormalizado, null, ct))
                throw new UseCaseValidationException("Ja existe categoria com esse slug.");

            var categoria = FaqCategoria.Criar(cmd.Nome, slugNormalizado, cmd.Descricao, cmd.Icone, cmd.Ordem);
            await repo.InserirCategoriaAsync(categoria, ct);
            await uow.CommitAsync();

            return new CriarFaqCategoriaResult(categoria.Id, categoria.Slug);
        }
    }
}
