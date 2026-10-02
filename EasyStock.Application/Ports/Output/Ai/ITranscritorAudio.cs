namespace EasyStock.Application.Ports.Output.Ai;

/// <summary>
/// #1398: transcreve o áudio recebido no WhatsApp. <see cref="Disponivel"/>=false (sem chave ou
/// desligado por config) mantém o comportamento anterior: o áudio fica sem texto.
/// </summary>
public interface ITranscritorAudio
{
    bool Disponivel { get; }

    /// <summary>Texto transcrito, ou null quando não há fala reconhecida.</summary>
    Task<string?> TranscreverAsync(byte[] audio, string mime, CancellationToken ct = default);
}
