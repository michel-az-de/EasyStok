using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;

/// <summary>
/// Adiciona Produto ao cardápio do storefront com metadata opcional. Garante:
/// (1) Storefront existe;
/// (2) Produto existe e pertence à mesma Empresa do storefront (tenant isolation);
/// (3) Produto ainda não está no cardápio (evita duplicata silenciosa).
/// </summary>
public sealed record AdicionarCardapioItemAdminCommand(
    Guid StorefrontId,
    Guid ProdutoId,
    double OrdemExibicao,
    bool Visivel,
    string? DescricaoPublica,
    string? Ingredientes,
    string? Alergenos,
    string? SugestaoMolho,
    string? TempoPreparo,
    string? FotoUrl,
    decimal? PrecoStorefront,
    string? Tag,
    string? PesoExibicao,
    string? FiltrosJson) : ICommand;

public sealed record AdicionarCardapioItemAdminResult(Guid ItemId, Guid StorefrontId, Guid ProdutoId);

public class AdicionarCardapioItemAdminUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository,
    IProdutoRepository produtoRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<AdicionarCardapioItemAdminCommand, AdicionarCardapioItemAdminResult>
{
    public async Task<AdicionarCardapioItemAdminResult> ExecuteAsync(AdicionarCardapioItemAdminCommand command)
    {
        var storefront = await storefrontRepository.GetByIdAsync(command.StorefrontId)
            ?? throw new StorefrontNaoEncontradoException();

        // Tenant isolation: produto tem que pertencer à mesma empresa do storefront.
        var produto = await produtoRepository.GetByIdAsync(storefront.EmpresaId, command.ProdutoId)
            ?? throw new UseCaseValidationException(
                "PRODUTO_INEXISTENTE",
                $"Produto {command.ProdutoId} não encontrado para a empresa {storefront.EmpresaId}.");

        var jaUsados = await cardapioRepository.GetProdutoIdsDoStorefrontAsync(command.StorefrontId);
        if (jaUsados.Contains(produto.Id))
            throw new UseCaseValidationException(
                "PRODUTO_JA_NO_CARDAPIO",
                $"Produto '{produto.Nome}' já está no cardápio deste storefront.");

        var item = CardapioItem.CriarAPartirDeProduto(command.StorefrontId, produto);

        // Aplica ordem e visibilidade fora dos defaults (factory cria com Visivel=false, ordem=0).
        if (command.OrdemExibicao > 0)
            item.DefinirOrdem(command.OrdemExibicao);

        if (command.Visivel)
            item.TornarVisivel();

        // Aplica metadata opcional. Cada parâmetro null = "não tocar"
        // (semantics consistente com AtualizarMetadata da entity).
        var temMetadata = command.DescricaoPublica is not null
            || command.Ingredientes is not null
            || command.Alergenos is not null
            || command.SugestaoMolho is not null
            || command.TempoPreparo is not null
            || command.FotoUrl is not null
            || command.PrecoStorefront.HasValue
            || command.Tag is not null
            || command.PesoExibicao is not null
            || command.FiltrosJson is not null;

        if (temMetadata)
        {
            item.AtualizarMetadata(
                descricaoPublica: command.DescricaoPublica,
                ingredientes: command.Ingredientes,
                alergenos: command.Alergenos,
                sugestaoMolho: command.SugestaoMolho,
                tempoPreparo: command.TempoPreparo,
                fotoUrl: command.FotoUrl,
                precoStorefront: command.PrecoStorefront,
                tag: command.Tag,
                filtrosJson: command.FiltrosJson,
                pesoExibicao: command.PesoExibicao);
        }

        await cardapioRepository.AddAsync(item);
        await unitOfWork.CommitAsync();

        return new AdicionarCardapioItemAdminResult(item.Id, command.StorefrontId, command.ProdutoId);
    }
}
