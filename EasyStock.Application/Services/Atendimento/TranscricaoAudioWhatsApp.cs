using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #1398: depois que o áudio do WhatsApp foi armazenado, lê do storage, transcreve e grava o texto
/// na <see cref="Mensagem"/>. Falha só loga: o armazenamento da mídia já está commitado.
/// </summary>
public sealed class TranscricaoAudioWhatsApp(
    ITranscritorAudio transcritor,
    IFileStorage fileStorage,
    IUnitOfWork unitOfWork,
    ILogger<TranscricaoAudioWhatsApp> logger)
{
    public async Task TranscreverAsync(Mensagem mensagem, CancellationToken ct = default)
    {
        if (mensagem.TipoConteudo != TipoConteudoMensagem.Audio
            || string.IsNullOrWhiteSpace(mensagem.MidiaChave)
            || !transcritor.Disponivel)
            return;

        try
        {
            var audio = await fileStorage.DownloadAsync(mensagem.MidiaChave, ct);
            var texto = await transcritor.TranscreverAsync(audio, mensagem.MidiaMime ?? "audio/ogg", ct);
            if (string.IsNullOrWhiteSpace(texto)) return;

            mensagem.RegistrarTranscricao(texto);
            await unitOfWork.CommitAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Transcrição de áudio WhatsApp falhou para a mensagem {MensagemId}.", mensagem.Id);
        }
    }
}
