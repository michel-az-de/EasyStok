using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio.EditarCardapioItemAdmin;

/// <summary>
/// Edição de metadata do CardapioItem. NÃO altera Visivel/Disponivel/Ordem
/// (têm use cases dedicados — Toggle e Reordenar). Permite limpar Tag passando
/// string vazia ("") — convertido para LimparTag() na entity.
/// </summary>
public sealed record EditarCardapioItemAdminCommand(
    Guid StorefrontId,
    Guid ItemId,
    string? NomePublico,    // override de nome (avulso ou vinculado)
    string? CategoriaTexto, // override de categoria
    string? DescricaoPublica,
    string? Ingredientes,
    string? Alergenos,
    string? SugestaoMolho,
    string? TempoPreparo,
    string? FotoUrl,
    decimal? PrecoStorefront,
    string? Tag,
    string? PesoExibicao,
    string? FiltrosJson,
    // EmpresaId: null = SuperAdmin; com valor = escopo do tenant (ADR-0031 §3, fecha IDOR).
    Guid? EmpresaId = null,
    // ADR-0035 (#652): opções do item guarda-chuva (reconciliação keyed-by-Id) e seção.
    // Opcoes null = não mexe; lista (mesmo vazia) = reconcilia. SecaoId null = não muda.
    IReadOnlyList<CardapioItemVariacaoInput>? Opcoes = null,
    Guid? SecaoId = null) : ICommand;

public sealed record EditarCardapioItemAdminResult(Guid ItemId);

public class EditarCardapioItemAdminUseCase(
    ICardapioItemRepository cardapioRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<EditarCardapioItemAdminCommand, EditarCardapioItemAdminResult>
{
    public async Task<EditarCardapioItemAdminResult> ExecuteAsync(EditarCardapioItemAdminCommand command)
    {
        var item = await cardapioRepository.GetByIdAndScopeAsync(command.StorefrontId, command.ItemId, command.EmpresaId)
            ?? throw new CardapioItemNaoEncontradoException(command.StorefrontId, command.ItemId);

        // Tag == "" significa "limpar". Tag null = "não tocar".
        // Demais campos seguem a mesma convenção da entity.
        if (command.Tag is "")
        {
            item.LimparTag();
        }

        item.AtualizarMetadata(
            nomePublico: command.NomePublico,
            categoriaTexto: command.CategoriaTexto,
            descricaoPublica: command.DescricaoPublica,
            ingredientes: command.Ingredientes,
            alergenos: command.Alergenos,
            sugestaoMolho: command.SugestaoMolho,
            tempoPreparo: command.TempoPreparo,
            fotoUrl: command.FotoUrl,
            precoStorefront: command.PrecoStorefront,
            tag: string.IsNullOrEmpty(command.Tag) ? null : command.Tag,
            filtrosJson: command.FiltrosJson,
            pesoExibicao: command.PesoExibicao);

        // ADR-0035 (#652): seção + reconciliação keyed-by-Id das opções (item rastreado via
        // GetByIdAndScope, que já inclui Variacoes — F3). null = não mexe.
        if (command.SecaoId.HasValue)
            item.DefinirSecao(command.SecaoId);
        CardapioVariacaoSync.Reconciliar(item, command.Opcoes);

        await cardapioRepository.UpdateAsync(item);
        await unitOfWork.CommitAsync();

        return new EditarCardapioItemAdminResult(item.Id);
    }
}
