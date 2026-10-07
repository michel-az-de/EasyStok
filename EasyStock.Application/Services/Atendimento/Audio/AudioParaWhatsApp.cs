namespace EasyStock.Application.Services.Atendimento.Audio;

/// <summary>Áudio pronto para a Cloud API: conteúdo, tipo MIME, extensão e se sai como nota de voz.</summary>
public sealed record AudioPreparado(byte[] Conteudo, string ContentType, string Extensao, bool NotaDeVoz);

/// <summary>
/// Prepara o áudio gravado no console para o WhatsApp (#1444). O formato vem dos bytes, não do tipo que o
/// navegador declarou. WebM/Opus (Chrome, Edge) é reempacotado em Ogg/Opus sem recodificar; Ogg/Opus (Firefox),
/// MP4 (Safari), MP3, AAC e AMR passam como vieram. Nota de voz só em Ogg/Opus mono, como a Meta exige.
/// </summary>
public static class AudioParaWhatsApp
{
    /// <summary>Limite de áudio da Cloud API.</summary>
    public const int TamanhoMaximo = 16 * 1024 * 1024;

    public static AudioPreparado Preparar(byte[] conteudo)
    {
        if (conteudo is null || conteudo.Length == 0)
            throw new UseCaseValidationException("Áudio vazio. Grave de novo.");
        if (conteudo.Length > TamanhoMaximo)
            throw new UseCaseValidationException("O áudio passa de 16 MB, o limite do WhatsApp. Grave um mais curto.");

        var inicio = conteudo.AsSpan();
        if (inicio.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
        {
            var faixa = WebmOpus.Ler(conteudo);
            return new AudioPreparado(OggOpus.Escrever(faixa), "audio/ogg", ".ogg", faixa.Canais == 1);
        }
        if (inicio.StartsWith("OggS"u8))
            return OggRecebido(conteudo);
        if (inicio.Length >= 8 && inicio[4..8].SequenceEqual("ftyp"u8))
            return new AudioPreparado(conteudo, "audio/mp4", ".m4a", false);
        if (inicio.StartsWith("#!AMR"u8))
            return new AudioPreparado(conteudo, "audio/amr", ".amr", false);
        if (inicio.StartsWith("ID3"u8))
            return new AudioPreparado(conteudo, "audio/mpeg", ".mp3", false);
        if (inicio.Length >= 2 && inicio[0] == 0xFF && (inicio[1] & 0xE0) == 0xE0)
        {
            // Sincronismo MPEG: camada 00 é ADTS (AAC); as outras são MP3.
            return (inicio[1] & 0x06) == 0
                ? new AudioPreparado(conteudo, "audio/aac", ".aac", false)
                : new AudioPreparado(conteudo, "audio/mpeg", ".mp3", false);
        }
        throw new UseCaseValidationException(
            "Esse formato de áudio não sai pelo WhatsApp. Aceitos: Ogg/Opus, WebM/Opus, MP4, MP3, AAC e AMR.");
    }

    /// <summary>Ogg da Meta precisa ser Opus: o <c>OpusHead</c> abre o primeiro pacote da primeira página.</summary>
    private static AudioPreparado OggRecebido(byte[] conteudo)
    {
        var segmentos = conteudo.Length > 26 ? conteudo[26] : 0;
        var corpo = 27 + segmentos;
        if (conteudo.Length < corpo + 19 || !conteudo.AsSpan(corpo, 8).SequenceEqual("OpusHead"u8))
            throw new UseCaseValidationException("Esse Ogg não é Opus, e o WhatsApp só aceita Ogg com Opus.");
        return new AudioPreparado(conteudo, "audio/ogg", ".ogg", conteudo[corpo + 9] == 1);
    }
}
