using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Tests.Helpers;

/// <summary>
/// O checkout e as janelas leem os pratos numa consulta só (#1725-8), pelo
/// <see cref="ICardapioItemRepository.ListarPorIdsAsync"/>. Este helper faz o substitute responder como o repositório
/// real a partir do que o teste já configurou em <see cref="ICardapioItemRepository.GetByIdAsync"/>: os itens
/// encontrados, só os que têm o id pedido. Quem troca o item de um id no teste continua mudando a resposta.
/// </summary>
internal static class CardapioItemRepositoryMockExtensions
{
    public static ICardapioItemRepository ListarPeloGetById(this ICardapioItemRepository repo)
    {
        repo.ListarPorIdsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                var itens = new List<CardapioItem>();
                foreach (var id in ci.ArgAt<IReadOnlyCollection<Guid>>(1))
                    if (await repo.GetByIdAsync(ci.ArgAt<Guid>(0), id, CancellationToken.None) is { } item && item.Id == id)
                        itens.Add(item);
                return (IReadOnlyList<CardapioItem>)itens;
            });
        return repo;
    }
}
