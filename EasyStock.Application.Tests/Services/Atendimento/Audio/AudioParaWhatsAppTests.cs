using System.Buffers.Binary;
using System.Text;
using EasyStock.Application.Services.Atendimento.Audio;

namespace EasyStock.Application.Tests.Services.Atendimento.Audio;

/// <summary>
/// #1444: o Chrome grava áudio em WebM/Opus e a Cloud API da Meta só aceita Ogg/Opus (nota de voz),
/// MP4, MPEG, AAC ou AMR. O WebM é reempacotado em Ogg sem recodificar; o resto passa como veio.
/// O Ogg produzido é conferido por um leitor independente (CRC calculado bit a bit, não pela tabela).
/// </summary>
public class AudioParaWhatsAppTests
{
    [Fact]
    public void WebmDoChromeViraOggOpusDeNotaDeVoz()
    {
        var audio = AudioParaWhatsApp.Preparar(AmostrasWebmChrome.MonoCurto);

        audio.ContentType.Should().Be("audio/ogg");
        audio.Extensao.Should().Be(".ogg");
        audio.NotaDeVoz.Should().BeTrue("Ogg/Opus mono é o formato de nota de voz da Meta");

        var ogg = LerOgg(audio.Conteudo);
        ogg.Paginas[0].Tipo.Should().Be(0x02, "a primeira página abre o fluxo (BOS)");
        ogg.Paginas[^1].Tipo.Should().Be(0x04, "a última página fecha o fluxo (EOS)");
        ogg.Pacotes[0].Should().Equal(OpusHeadDe(AmostrasWebmChrome.MonoCurto), "o OpusHead é o CodecPrivate do WebM");
        Encoding.ASCII.GetString(ogg.Pacotes[1], 0, 8).Should().Be("OpusTags");
        ogg.Pacotes.Should().HaveCount(2 + 6, "o ffmpeg extrai 6 pacotes Opus desta gravação");
        ogg.Pacotes.Skip(2).Should().AllSatisfy(p => p[0].Should().Be(0xFB, "pacote CELT 20 ms, código 3"));
        ogg.Paginas[^1].Granulo.Should().Be(6 * 2880, "6 pacotes de 3 quadros de 20 ms a 48 kHz");
    }

    [Fact]
    public void WebmComSegmentoEClusterDeTamanhoDesconhecidoTambemConverte()
    {
        var audio = AudioParaWhatsApp.Preparar(AmostrasWebmChrome.MonoFatiado);

        var ogg = LerOgg(audio.Conteudo);
        ogg.Pacotes.Should().HaveCount(2 + 8);
        ogg.Paginas[^1].Granulo.Should().Be(8 * 2880);
        audio.NotaDeVoz.Should().BeTrue();
    }

    [Fact]
    public void EstereoViraOggMasNaoSaiComoNotaDeVoz()
    {
        var audio = AudioParaWhatsApp.Preparar(AmostrasWebmChrome.Estereo);

        audio.ContentType.Should().Be("audio/ogg");
        audio.NotaDeVoz.Should().BeFalse("a Meta só toca nota de voz Ogg/Opus mono");
        LerOgg(audio.Conteudo).Pacotes[0][9].Should().Be(2, "o OpusHead diz dois canais");
    }

    [Fact]
    public void OggOpusDoFirefoxPassaComoVeio()
    {
        var ogg = AudioParaWhatsApp.Preparar(AmostrasWebmChrome.MonoCurto).Conteudo;

        var audio = AudioParaWhatsApp.Preparar(ogg);

        audio.Conteudo.Should().BeSameAs(ogg);
        audio.ContentType.Should().Be("audio/ogg");
        audio.NotaDeVoz.Should().BeTrue();
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0x20, 0x66, 0x74, 0x79, 0x70, 0x4D, 0x34, 0x41, 0x20 }, "audio/mp4", ".m4a")]
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 4, 0, 0, 0, 0, 0, 0 }, "audio/mpeg", ".mp3")]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0x64, 0, 0 }, "audio/mpeg", ".mp3")]
    [InlineData(new byte[] { 0xFF, 0xF1, 0x50, 0x80, 0, 0 }, "audio/aac", ".aac")]
    [InlineData(new byte[] { 0x23, 0x21, 0x41, 0x4D, 0x52, 0x0A, 0, 0 }, "audio/amr", ".amr")]
    public void FormatosAceitosPelaMetaPassamSemConverter(byte[] conteudo, string contentType, string extensao)
    {
        var audio = AudioParaWhatsApp.Preparar(conteudo);

        audio.Conteudo.Should().BeSameAs(conteudo);
        audio.ContentType.Should().Be(contentType);
        audio.Extensao.Should().Be(extensao);
        audio.NotaDeVoz.Should().BeFalse();
    }

    [Fact]
    public void FormatoDesconhecidoEhRecusadoComMotivo()
    {
        var acao = () => AudioParaWhatsApp.Preparar(Encoding.ASCII.GetBytes("RIFF....WAVEfmt "));

        acao.Should().Throw<UseCaseValidationException>().WithMessage("*formato*");
    }

    [Fact]
    public void VazioEhRecusado()
    {
        var acao = () => AudioParaWhatsApp.Preparar([]);

        acao.Should().Throw<UseCaseValidationException>();
    }

    [Fact]
    public void MaiorQueOLimiteDaMetaEhRecusado()
    {
        var grande = new byte[AudioParaWhatsApp.TamanhoMaximo + 1];
        grande[0] = 0x23; grande[1] = 0x21; grande[2] = 0x41; grande[3] = 0x4D; grande[4] = 0x52;

        var acao = () => AudioParaWhatsApp.Preparar(grande);

        acao.Should().Throw<UseCaseValidationException>().WithMessage("*16 MB*");
    }

    [Fact]
    public void WebmSemOpusEhRecusado()
    {
        var webm = (byte[])AmostrasWebmChrome.MonoCurto.Clone();
        var indice = IndiceDe(webm, Encoding.ASCII.GetBytes("A_OPUS"));
        Encoding.ASCII.GetBytes("A_VORB").CopyTo(webm, indice);

        var acao = () => AudioParaWhatsApp.Preparar(webm);

        acao.Should().Throw<UseCaseValidationException>().WithMessage("*Opus*");
    }

    [Fact]
    public void CrcDoOggConfereComOVetorPadrao() =>
        // CRC-32 de "123456789" com polinômio 0x04C11DB7, sem reflexão, início 0 e sem xor final.
        CrcOggBitABit(Encoding.ASCII.GetBytes("123456789")).Should().Be(0x89A1897Fu);

    // ── leitor independente de Ogg ────────────────────────────────────

    private sealed record Pagina(byte Tipo, long Granulo);

    private sealed record Ogg(List<Pagina> Paginas, List<byte[]> Pacotes);

    private static Ogg LerOgg(byte[] dados)
    {
        var paginas = new List<Pagina>();
        var pacotes = new List<byte[]>();
        var atual = new List<byte>();
        var p = 0;
        while (p < dados.Length)
        {
            Encoding.ASCII.GetString(dados, p, 4).Should().Be("OggS", $"página na posição {p}");
            var segmentos = dados[p + 26];
            var tamanho = 0;
            for (var i = 0; i < segmentos; i++) tamanho += dados[p + 27 + i];
            var total = 27 + segmentos + tamanho;

            var pagina = dados.AsSpan(p, total).ToArray();
            var crcGravado = BinaryPrimitives.ReadUInt32LittleEndian(pagina.AsSpan(22));
            pagina[22] = pagina[23] = pagina[24] = pagina[25] = 0;
            CrcOggBitABit(pagina).Should().Be(crcGravado, $"CRC da página {paginas.Count}");

            paginas.Add(new Pagina(dados[p + 5], BinaryPrimitives.ReadInt64LittleEndian(dados.AsSpan(p + 6))));
            var corpo = p + 27 + segmentos;
            for (var i = 0; i < segmentos; i++)
            {
                var segmento = dados[p + 27 + i];
                atual.AddRange(dados.AsSpan(corpo, segmento).ToArray());
                corpo += segmento;
                if (segmento < 255) { pacotes.Add([.. atual]); atual.Clear(); }
            }
            p += total;
        }
        return new Ogg(paginas, pacotes);
    }

    private static uint CrcOggBitABit(byte[] dados)
    {
        uint crc = 0;
        foreach (var b in dados)
        {
            crc ^= (uint)b << 24;
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
        }
        return crc;
    }

    private static byte[] OpusHeadDe(byte[] webm)
    {
        var inicio = IndiceDe(webm, Encoding.ASCII.GetBytes("OpusHead"));
        return webm.AsSpan(inicio, 19).ToArray();
    }

    private static int IndiceDe(byte[] dados, byte[] procurado) => dados.AsSpan().IndexOf(procurado);
}
