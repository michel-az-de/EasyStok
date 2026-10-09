using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.EditarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ListarCardapioAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ReordenarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleVisibilidadeCardapioItemAdmin;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

/// <param name="Linha">null = não mexe.</param>
public sealed record DadosItemCardapio(string? Nome, LinhaProduto? Linha, string? Porcao, decimal? Preco, string? Categoria);

public sealed record ItemForaDoCardapio(Guid CardapioItemId, string Nome, decimal Preco, string? Porcao, string? Categoria, string? FotoUrl);

public sealed record VisibilidadeItemResult(Guid CardapioItemId, bool Visivel);

/// <summary>Linha da tela de gestão do cardápio (M1.1): tudo o que a lista mostra e liga.</summary>
/// <param name="ControlaSaldo">Item ligado a um produto do estoque (avulso não tem saldo).</param>
public sealed record ItemGestaoCardapio(
    Guid CardapioItemId, string Nome, LinhaProduto Linha, string? Porcao, decimal Preco, string? Categoria,
    string? FotoUrl, bool Visivel, bool Disponivel, double Ordem, bool ControlaSaldo);

public sealed record OrdemItemResult(Guid CardapioItemId, double Ordem);

/// <summary>
/// Itens do cardápio pelo console (#1241, F11, S45). Decisão do Felipe (08/10/2026): incluir, editar
/// e tirar item são do Gerente. Reusa os use cases da vitrine (mesmas validações e guardas de HTML)
/// na vitrine da empresa logada. "Tirar" é esconder (visível = falso), nunca apagar: pedido antigo
/// continua achando o item, e a lista de fora deixa repor.
/// </summary>
public sealed class ItensDoCardapioComandaUseCase(
    IStorefrontRepository storefrontRepository,
    AdicionarCardapioItemAdminUseCase adicionar,
    EditarCardapioItemAdminUseCase editar,
    ToggleVisibilidadeCardapioItemAdminUseCase toggleVisivel,
    ListarCardapioAdminUseCase listar,
    ReordenarCardapioItemAdminUseCase reordenar,
    ICardapioItemRepository cardapioRepository)
{
    public async Task<AdicionarCardapioItemAdminResult> IncluirAsync(Guid empresaId, DadosItemCardapio dados, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dados.Nome))
            throw new UseCaseValidationException("Informe o nome do item.");
        if (dados.Preco is not > 0)
            throw new UseCaseValidationException("Informe o preço do item.");
        var storefrontId = await VitrineAsync(empresaId, ct);

        // Item avulso (sem produto do estoque), visível. Ordem alta: entra no fim da lista.
        return await adicionar.ExecuteAsync(new AdicionarCardapioItemAdminCommand(
            storefrontId, null, dados.Nome.Trim(), dados.Categoria, 10_000, true,
            null, null, null, null, null, null, dados.Preco, null, dados.Porcao, null,
            empresaId, Linha: dados.Linha));
    }

    public async Task EditarAsync(Guid empresaId, Guid itemId, DadosItemCardapio dados, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        await editar.ExecuteAsync(new EditarCardapioItemAdminCommand(
            storefrontId, itemId, dados.Nome?.Trim(), dados.Categoria,
            null, null, null, null, null, null, dados.Preco, null, dados.Porcao, null,
            empresaId, Linha: dados.Linha));
    }

    public async Task<VisibilidadeItemResult> DefinirVisivelAsync(Guid empresaId, Guid itemId, bool visivel, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var item = await cardapioRepository.GetByIdAndScopeAsync(storefrontId, itemId, empresaId, ct)
            ?? throw new CardapioItemNaoEncontradoException(storefrontId, itemId);
        if (item.Visivel == visivel) return new VisibilidadeItemResult(item.Id, item.Visivel);

        var r = await toggleVisivel.ExecuteAsync(new ToggleVisibilidadeCardapioItemAdminCommand(storefrontId, itemId, empresaId));
        return new VisibilidadeItemResult(r.ItemId, r.VisivelAgora);
    }

    /// <summary>Itens escondidos (fora do cardápio de hoje), para o console mostrar "Repor".</summary>
    public async Task<IReadOnlyList<ItemForaDoCardapio>> ListarForaAsync(Guid empresaId, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var lista = await listar.ExecuteAsync(new ListarCardapioAdminCommand(storefrontId, empresaId));
        return lista.Itens
            .Where(i => !i.Visivel)
            .Select(i => new ItemForaDoCardapio(i.Id, i.NomeEfetivo, i.PrecoEfetivo, i.PesoExibicao, i.CategoriaTexto, i.FotoUrl))
            .ToList();
    }

    /// <summary>
    /// Gestão do cardápio (M1.1): todos os itens da vitrine, inclusive os ocultos do site e os
    /// desligados do dia (RN-16: desligar não tira da lista), na ordem de exibição.
    /// </summary>
    public async Task<IReadOnlyList<ItemGestaoCardapio>> ListarGestaoAsync(Guid empresaId, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var itens = await cardapioRepository.GetTodosDoStorefrontAsync(storefrontId, ct);
        return itens
            .Select(i => new ItemGestaoCardapio(
                i.Id, i.NomeEfetivo() ?? "(sem nome)", i.Linha, i.PesoExibicao, i.PrecoEfetivo(), i.CategoriaEfetiva(),
                i.FotoUrl, i.Visivel, i.Disponivel, i.OrdemExibicao, i.ProdutoId.HasValue))
            .ToList();
    }

    /// <summary>Ordem por arrastar: o console manda o valor entre os vizinhos (ordem é double).</summary>
    public async Task<OrdemItemResult> DefinirOrdemAsync(Guid empresaId, Guid itemId, double novaOrdem, CancellationToken ct = default)
    {
        var storefrontId = await VitrineAsync(empresaId, ct);
        var r = await reordenar.ExecuteAsync(new ReordenarCardapioItemAdminCommand(storefrontId, itemId, novaOrdem, empresaId));
        return new OrdemItemResult(r.ItemId, r.Ordem);
    }

    private async Task<Guid> VitrineAsync(Guid empresaId, CancellationToken ct)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException();
        return storefront.Id;
    }
}
