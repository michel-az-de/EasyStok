using System.Globalization;
using System.Text;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Campanhas;

namespace EasyStock.Application.UseCases.Campanhas.Interesse;

public sealed record RegistrarInteresseItemCommand(
    Guid EmpresaId, Guid ClienteId, Guid? CardapioItemId, string? Descricao, string Origem);

public sealed record InteresseItemResult(
    Guid Id, Guid ClienteId, Guid? CardapioItemId, string? Descricao, string Origem, DateTime RegistradoEm);

public sealed class ClienteNaoEncontradoParaInteresseException(Guid clienteId)
    : Exception($"Cliente {clienteId} não encontrado nesta empresa.");

/// <summary>
/// S31: registra que o cliente quer um item indisponível. O item do cardápio é identificado pelo id
/// (só vale se for da loja da empresa) ou pelo nome exato, sem acento nem caixa; sem item, fica a
/// descrição livre. Com item e sem descrição, guarda o nome do item: se ele for removido do
/// cardápio, o interesse continua legível.
/// </summary>
public sealed class RegistrarInteresseItemUseCase(
    IClienteRepository clientes,
    IStorefrontRepository storefronts,
    ICardapioItemRepository cardapio,
    IInteresseItemRepository interesses,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    /// <summary>Console da dona: registra e grava.</summary>
    public async Task<InteresseItemResult> ExecuteAsync(RegistrarInteresseItemCommand command, CancellationToken ct = default)
    {
        var interesse = await RegistrarAsync(command, relogio.GetUtcNow().UtcDateTime, ct);
        await unitOfWork.CommitAsync();
        return Mapear(interesse);
    }

    /// <summary>
    /// Adiciona sem gravar: a ferramenta do agente usa este caminho porque o commit é do turno.
    /// </summary>
    /// <exception cref="ClienteNaoEncontradoParaInteresseException">Cliente de outra empresa ou inexistente.</exception>
    /// <exception cref="UseCaseValidationException">Sem item identificável e sem descrição.</exception>
    public async Task<InteresseItem> RegistrarAsync(RegistrarInteresseItemCommand command, DateTime em, CancellationToken ct = default)
    {
        _ = await clientes.GetByIdAsync(command.EmpresaId, command.ClienteId)
            ?? throw new ClienteNaoEncontradoParaInteresseException(command.ClienteId);

        var descricao = string.IsNullOrWhiteSpace(command.Descricao) ? null : command.Descricao.Trim();
        var storefront = await storefronts.GetByEmpresaAsync(command.EmpresaId, ct);

        Domain.Entities.Storefront.CardapioItem? item = null;
        if (storefront is not null && command.CardapioItemId is { } itemId && itemId != Guid.Empty)
            item = await cardapio.GetByIdAndScopeAsync(storefront.Id, itemId, command.EmpresaId, ct);
        if (item is null && storefront is not null && descricao is not null)
        {
            var alvo = Normalizar(descricao);
            var mesmos = (await cardapio.GetTodosDoStorefrontAsync(storefront.Id, ct))
                .Where(i => i.NomeEfetivo() is { } nome && Normalizar(nome) == alvo)
                .Take(2)
                .ToList();
            item = mesmos.Count == 1 ? mesmos[0] : null;
        }

        if (item is null && descricao is null)
            throw new UseCaseValidationException("Item do cardápio não encontrado: descreva o que o cliente quer.");

        InteresseItem interesse;
        try
        {
            interesse = InteresseItem.Registrar(
                command.EmpresaId, command.ClienteId, item?.Id, descricao ?? item?.NomeEfetivo(), command.Origem, em);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        await interesses.AddAsync(interesse, ct);
        return interesse;
    }

    internal static InteresseItemResult Mapear(InteresseItem i) =>
        new(i.Id, i.ClienteId, i.CardapioItemId, i.Descricao, i.Origem, i.RegistradoEm);

    private static string Normalizar(string texto)
    {
        var decomposto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }
}

/// <summary>
/// S31: clientes com interesse aberto no item, para a dona montar a campanha ou avisar na mão.
/// Só sugere; não envia nada.
/// </summary>
public sealed class ListarSugestoesInteresseUseCase(IInteresseItemRepository interesses)
{
    public Task<IReadOnlyList<InteresseAbertoCliente>> ExecuteAsync(Guid empresaId, Guid cardapioItemId, CancellationToken ct = default) =>
        interesses.ListarAbertosDoItemAsync(empresaId, cardapioItemId, ct);
}
