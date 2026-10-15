using EasyStock.Application.Ports.Output.Ai;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>#1398: padrão sem provedor configurado. O áudio segue sem transcrição.</summary>
public sealed class TranscritorAudioNulo : ITranscritorAudio
{
    public bool Disponivel => false;

    public Task<string?> TranscreverAsync(byte[] audio, string mime, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
