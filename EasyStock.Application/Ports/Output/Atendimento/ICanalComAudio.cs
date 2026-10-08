namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// Canal que envia áudio gravado pela dona no console (#1444). Interface à parte de
/// <see cref="ICanalMensageria"/>, como <see cref="ICanalComTagHumana"/>, para não mudar os adaptadores
/// que ainda não mandam áudio. <paramref name="urlPublica"/> é HTTPS pública: o canal busca o arquivo por lá.
/// </summary>
public interface ICanalComAudio
{
    /// <summary><paramref name="notaDeVoz"/>: só Ogg/Opus mono sai como nota de voz no WhatsApp.</summary>
    Task<string> EnviarAudioAsync(string contatoIdExterno, string urlPublica, bool notaDeVoz, CancellationToken ct = default);
}
