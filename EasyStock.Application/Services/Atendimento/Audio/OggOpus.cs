using System.Buffers.Binary;
using System.Text;

namespace EasyStock.Application.Services.Atendimento.Audio;

/// <summary>
/// Escreve pacotes Opus num Ogg (RFC 7845) sem recodificar (#1444): página com o <c>OpusHead</c> (BOS),
/// página com o <c>OpusTags</c> e páginas de áudio cujo granule é o total de amostras a 48 kHz já
/// concluídas; a última fecha o fluxo (EOS). É o formato de nota de voz que a Cloud API da Meta aceita.
/// </summary>
public static class OggOpus
{
    private const byte InicioDoFluxo = 0x02;
    private const byte FimDoFluxo = 0x04;
    private const int PacotesPorPagina = 50; // 1 a 3 s de áudio por página
    private const uint NumeroDeSerie = 0x45535442; // "ESTB": um só fluxo lógico por arquivo

    private static readonly uint[] TabelaCrc = MontarTabelaCrc();

    public static byte[] Escrever(FaixaOpus faixa)
    {
        using var saida = new MemoryStream();
        uint sequencia = 0;
        EscreverPagina(saida, [faixa.OpusHead], InicioDoFluxo, 0, sequencia++);
        EscreverPagina(saida, [OpusTags()], 0, 0, sequencia++);

        long granulo = 0;
        var pagina = new List<ReadOnlyMemory<byte>>();
        var segmentos = 0;
        for (var i = 0; i < faixa.Pacotes.Count; i++)
        {
            var pacote = faixa.Pacotes[i];
            var segmentosDoPacote = pacote.Length / 255 + 1;
            if (pagina.Count > 0 && (pagina.Count == PacotesPorPagina || segmentos + segmentosDoPacote > 255))
            {
                EscreverPagina(saida, pagina, 0, granulo, sequencia++);
                pagina = [];
                segmentos = 0;
            }
            pagina.Add(pacote);
            segmentos += segmentosDoPacote;
            granulo += AmostrasDoPacote(pacote.Span);
        }
        EscreverPagina(saida, pagina, FimDoFluxo, granulo, sequencia);
        return saida.ToArray();
    }

    /// <summary>
    /// Amostras a 48 kHz de um pacote Opus pelo byte TOC (RFC 6716 §3.1): duração do quadro pela
    /// configuração e quantidade de quadros pelo código.
    /// </summary>
    public static int AmostrasDoPacote(ReadOnlySpan<byte> pacote)
    {
        if (pacote.IsEmpty) return 0;
        var toc = pacote[0];
        var configuracao = toc >> 3;
        var porQuadro = configuracao switch
        {
            < 12 => new[] { 480, 960, 1920, 2880 }[configuracao & 3], // SILK: 10, 20, 40, 60 ms
            < 16 => (configuracao & 1) == 0 ? 480 : 960, // híbrido: 10, 20 ms
            _ => new[] { 120, 240, 480, 960 }[configuracao & 3], // CELT: 2,5, 5, 10, 20 ms
        };
        var quadros = (toc & 3) switch
        {
            0 => 1,
            1 or 2 => 2,
            _ => pacote.Length > 1 ? pacote[1] & 0x3F : 0,
        };
        return porQuadro * quadros;
    }

    private static byte[] OpusTags()
    {
        var fornecedor = Encoding.ASCII.GetBytes("EasyStok");
        var tags = new byte[8 + 4 + fornecedor.Length + 4];
        "OpusTags"u8.CopyTo(tags);
        BinaryPrimitives.WriteUInt32LittleEndian(tags.AsSpan(8), (uint)fornecedor.Length);
        fornecedor.CopyTo(tags, 12);
        // Sem comentários de usuário: os 4 bytes finais ficam zerados.
        return tags;
    }

    private static void EscreverPagina(
        MemoryStream saida, IReadOnlyList<ReadOnlyMemory<byte>> pacotes, byte tipo, long granulo, uint sequencia)
    {
        var segmentos = new List<byte>();
        foreach (var pacote in pacotes)
        {
            var restante = pacote.Length;
            while (restante >= 255) { segmentos.Add(255); restante -= 255; }
            segmentos.Add((byte)restante); // segmento menor que 255 fecha o pacote
        }
        if (segmentos.Count > 255)
            throw new InvalidOperationException("Página Ogg com mais de 255 segmentos.");

        var corpo = pacotes.Sum(p => p.Length);
        var pagina = new byte[27 + segmentos.Count + corpo];
        "OggS"u8.CopyTo(pagina);
        pagina[4] = 0; // versão
        pagina[5] = tipo;
        BinaryPrimitives.WriteInt64LittleEndian(pagina.AsSpan(6), granulo);
        BinaryPrimitives.WriteUInt32LittleEndian(pagina.AsSpan(14), NumeroDeSerie);
        BinaryPrimitives.WriteUInt32LittleEndian(pagina.AsSpan(18), sequencia);
        pagina[26] = (byte)segmentos.Count;
        segmentos.ToArray().CopyTo(pagina, 27);
        var p = 27 + segmentos.Count;
        foreach (var pacote in pacotes)
        {
            pacote.Span.CopyTo(pagina.AsSpan(p));
            p += pacote.Length;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(pagina.AsSpan(22), Crc(pagina));
        saida.Write(pagina);
    }

    /// <summary>CRC-32 do Ogg: polinômio 0x04C11DB7, sem reflexão, início 0, sem xor final.</summary>
    private static uint Crc(ReadOnlySpan<byte> dados)
    {
        uint crc = 0;
        foreach (var b in dados) crc = (crc << 8) ^ TabelaCrc[(crc >> 24) ^ b];
        return crc;
    }

    private static uint[] MontarTabelaCrc()
    {
        var tabela = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var r = i << 24;
            for (var j = 0; j < 8; j++) r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04C11DB7 : r << 1;
            tabela[i] = r;
        }
        return tabela;
    }
}
