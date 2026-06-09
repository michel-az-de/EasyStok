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
    string? FiltrosJson) : ICommand;

public sealed record EditarCardapioItemAdminResult(Guid ItemId);

public class EditarCardapioItemAdminUseCase(
    ICardapioItemRepository cardapioRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<EditarCardapioItemAdminCommand, EditarCardapioItemAdminResult>
{
    public async Task<EditarCardapioItemAdminResult> ExecuteAsync(EditarCardapioItemAdminCommand command)
    {
        var item = await cardapioRepository.GetByIdAsync(command.StorefrontId, command.ItemId)
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

        await cardapioRepository.UpdateAsync(item);
        await unitOfWork.CommitAsync();

        return new EditarCardapioItemAdminResult(item.Id);
    }
}
