using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Producao;

/// <summary>
/// Liga o prato avulso do cardápio (sem produto) a um produto de estoque na categoria "Cardápio"
/// (D-M1-01: o que a casa produz é vinculado). Usado pela produção do dia (M2.2, #1491) e, desde a
/// #2235 (banca A1), pela receita: montar a receita antes da primeira produção liga o prato.
/// </summary>
public sealed class LigarPratoAoEstoqueUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository,
    ICategoriaRepository categoriaRepository,
    IProdutoRepository produtoRepository,
    CadastrarProdutoUseCase cadastrarProduto,
    IUnitOfWork unitOfWork)
{
    public const string CategoriaPratos = "Cardápio";

    /// <summary>Liga um prato e devolve o produto dele. Prato já ligado devolve o produto que tem.</summary>
    public async Task<Guid> ExecuteAsync(Guid empresaId, Guid cardapioItemId, Guid usuarioId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException();
        var prato = await cardapioRepository.GetByIdAndScopeAsync(storefront.Id, cardapioItemId, empresaId, ct)
            ?? throw new CardapioItemNaoEncontradoException(storefront.Id, cardapioItemId);
        if (prato.EstaArquivado)
            throw new UseCaseValidationException($"\"{prato.NomeEfetivo()}\" está fora do cardápio: reponha antes de montar a receita.");

        await LigarAsync(empresaId, usuarioId, [prato]);
        return prato.ProdutoId!.Value;
    }

    /// <summary>Liga os pratos ainda sem produto. Os já ligados ficam como estão.</summary>
    public async Task LigarAsync(Guid empresaId, Guid usuarioId, IReadOnlyList<CardapioItem> pratos)
    {
        var avulsos = pratos.Where(p => !p.ProdutoId.HasValue).Distinct().ToList();
        if (avulsos.Count == 0) return;

        var categoriaId = await CategoriaDeEstoque.ObterOuCriarAsync(categoriaRepository, unitOfWork, empresaId,
            CategoriaPratos, "Pratos produzidos pela casa (criada pela produção do console)");
        foreach (var prato in avulsos)
        {
            // O cadastro recusa nome repetido com uma frase de cadastro; aqui a loja precisa saber o que fazer.
            var nome = Exibicao(prato.NomeEfetivo());
            if (await produtoRepository.ExistsNomeAsync(empresaId, nome))
                throw new UseCaseValidationException(
                    $"Já existe no estoque um produto chamado \"{nome}\". Mude o nome do prato no Cardápio ou o do produto no Estoque e salve de novo.");
            var produto = await cadastrarProduto.ExecuteAsync(new CadastrarProdutoCommand(
                empresaId, categoriaId, null, nome, null, null, TipoProduto.Alimento,
                null, null, true, null, null, prato.PrecoEfetivo(), null, null, null, null, null, null, usuarioId));
            prato.VincularProduto(produto.ProdutoId);
        }
        await unitOfWork.CommitAsync();
    }

    /// <summary>O nome do avulso é gravado em minúsculo (factory do item): o produto e a tela mostram com inicial maiúscula.</summary>
    public static string Exibicao(string? nome)
    {
        var limpo = (nome ?? "Prato").Trim();
        return limpo.Length == 0 ? "Prato" : char.ToUpperInvariant(limpo[0]) + limpo[1..];
    }
}
