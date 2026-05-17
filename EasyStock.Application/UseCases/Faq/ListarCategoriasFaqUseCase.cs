using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Application.UseCases.Faq
{
    public sealed class ListarCategoriasFaqUseCase(IFaqRepository faqRepo)
    {
        public async Task<IReadOnlyList<FaqCategoriaDto>> ExecuteAsync(CancellationToken ct = default)
        {
            var categorias = await faqRepo.ListarCategoriasPublicasAsync(ct);

            return categorias
                .Select(c => new FaqCategoriaDto(
                    c.Id,
                    c.Nome,
                    c.Slug,
                    c.Descricao,
                    c.Icone,
                    c.Ordem,
                    c.Publica,
                    c.Itens?.Count(i => i.Status == Domain.Enums.FaqStatus.Publicado) ?? 0))
                .ToList();
        }
    }
}
