using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record EnviarImagemCardapioConsoleCommand(
    Guid EmpresaId, Guid UsuarioId, Guid ConversaId, Guid CardapioItemId, int Indice, string? Legenda);

/// <summary>
/// Foto da galeria do cardápio mandada pelo console (#1437). O navegador não baixa a foto: o item é
/// resolvido na vitrine da empresa do token, a chave sai da URL gravada sem depender do host
/// (<see cref="StorageKeyExtractor"/>) e o arquivo é lido do storage. O envio é o mesmo da foto do
/// computador (<see cref="EnviarMensagemConsoleUseCase.EnviarImagemAsync"/>): converte para JPEG,
/// sobe em <c>atendimento/</c>, envia pelo canal e grava a mensagem da dona.
///
/// <para>
/// <see cref="EnviarImagemCardapioConsoleCommand.Indice"/> segue a lista que o console mostra: a
/// galeria do item ou, sem galeria, só a capa.
/// </para>
/// </summary>
public sealed class EnviarImagemCardapioConsoleUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioItemRepository,
    IFileStorage fileStorage,
    EnviarMensagemConsoleUseCase enviarMensagem)
{
    private static readonly Dictionary<string, string> MimePorExtensao = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
    };

    public async Task<MensagemAtendimentoResult> ExecuteAsync(EnviarImagemCardapioConsoleCommand command, CancellationToken ct = default)
    {
        var storefront = await storefrontRepository.GetByEmpresaAsync(command.EmpresaId, ct)
            ?? throw new UseCaseValidationException("Sua vitrine ainda nao foi criada.");
        var item = await cardapioItemRepository.GetByIdAndScopeAsync(storefront.Id, command.CardapioItemId, command.EmpresaId, ct)
            ?? throw new FotoCardapioNaoEncontradaException("Item do cardápio não encontrado.");

        var fotos = FotosDoItem(item);
        if (command.Indice < 0 || command.Indice >= fotos.Count)
            throw new UseCaseValidationException("Foto do cardápio não encontrada para este item.");

        var chave = StorageKeyExtractor.Extract(fotos[command.Indice]);
        if (chave is null || !await fileStorage.ExistsAsync(chave, ct))
            throw new FotoCardapioNaoEncontradaException("Arquivo da foto do cardápio não encontrado.");

        var conteudo = await fileStorage.DownloadAsync(chave, ct);
        var nome = Path.GetFileName(chave);
        var mime = MimePorExtensao.GetValueOrDefault(Path.GetExtension(nome), "application/octet-stream");

        return await enviarMensagem.EnviarImagemAsync(new EnviarImagemConsoleCommand(
            command.EmpresaId, command.UsuarioId, command.ConversaId, nome, mime, conteudo, command.Legenda), ct);
    }

    /// <summary>Mesma lista do console (comandaApi.js): galeria, ou a capa quando não há galeria.</summary>
    private static IReadOnlyList<string> FotosDoItem(CardapioItem item)
    {
        var galeria = item.ObterFotosGaleria();
        if (galeria.Count > 0) return galeria;
        return string.IsNullOrWhiteSpace(item.FotoUrl) ? [] : [item.FotoUrl];
    }
}

/// <summary>Item de outra empresa, inexistente, ou foto que não está mais no storage: 404, sem vazar existência.</summary>
public sealed class FotoCardapioNaoEncontradaException(string mensagem) : Exception(mensagem);
