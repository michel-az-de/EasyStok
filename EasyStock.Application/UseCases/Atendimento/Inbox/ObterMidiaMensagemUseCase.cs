using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Storage;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record ObterMidiaMensagemQuery(Guid EmpresaId, Guid ConversaId, Guid MensagemId);

public sealed record MidiaMensagemResult(byte[] Conteudo, string Mime);

/// <summary>
/// Arquivo da mensagem para o console (#1287): a mídia recebida do cliente mora no storage privado
/// (<see cref="Services.Atendimento.ArmazenadorMidiaWhatsApp"/>) e só sai por aqui, autenticada.
/// Nula quando a mensagem não é da empresa e da conversa, não tem arquivo ou o arquivo sumiu do storage.
/// </summary>
public sealed class ObterMidiaMensagemUseCase(IConversaRepository conversaRepository, IFileStorage fileStorage)
{
    private const string MimePadrao = "application/octet-stream";

    public async Task<MidiaMensagemResult?> ExecuteAsync(ObterMidiaMensagemQuery query, CancellationToken ct = default)
    {
        var mensagem = await conversaRepository.ObterMensagemAsync(query.EmpresaId, query.ConversaId, query.MensagemId, ct);
        if (mensagem?.MidiaChave is not { } chave) return null;
        if (!await fileStorage.ExistsAsync(chave, ct)) return null;

        var conteudo = await fileStorage.DownloadAsync(chave, ct);
        return new MidiaMensagemResult(conteudo, string.IsNullOrWhiteSpace(mensagem.MidiaMime) ? MimePadrao : mensagem.MidiaMime);
    }
}
