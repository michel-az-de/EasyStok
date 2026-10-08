namespace EasyStock.Application.Services.Atendimento.Audio;

/// <summary>Faixa Opus de um WebM: o cabeçalho <c>OpusHead</c> e os pacotes na ordem gravada.</summary>
public sealed record FaixaOpus(byte[] OpusHead, IReadOnlyList<ReadOnlyMemory<byte>> Pacotes)
{
    public int Canais => OpusHead[9];
}

/// <summary>
/// Leitor mínimo de WebM (Matroska/EBML) para tirar a faixa Opus que o <c>MediaRecorder</c> do Chrome grava
/// (#1444). Desce em Segment, Cluster, Tracks, TrackEntry e BlockGroup, inclusive com tamanho desconhecido (o
/// Chrome grava assim quando o navegador entrega em fatias), e pula o resto pelo tamanho. Não decodifica nada.
/// </summary>
public static class WebmOpus
{
    private const uint IdSegmento = 0x18538067;
    private const uint IdCluster = 0x1F43B675;
    private const uint IdFaixas = 0x1654AE6B;
    private const uint IdFaixa = 0xAE;
    private const uint IdGrupoDeBloco = 0xA0;
    private const uint IdAudio = 0xE1;
    private const uint IdNumeroDaFaixa = 0xD7;
    private const uint IdCodec = 0x86;
    private const uint IdCodecPrivado = 0x63A2;
    private const uint IdCanais = 0x9F;
    private const uint IdBlocoSimples = 0xA3;
    private const uint IdBloco = 0xA1;

    private static readonly HashSet<uint> Conteineres = [IdSegmento, IdCluster, IdFaixas, IdFaixa, IdGrupoDeBloco, IdAudio];

    private sealed class Faixa
    {
        public ulong Numero;
        public string? Codec;
        public byte[]? CodecPrivado;
        public int Canais = 1;
    }

    public static FaixaOpus Ler(ReadOnlySpan<byte> dados)
    {
        var faixas = new List<Faixa>();
        var blocos = new List<(ulong Faixa, int Inicio, int Tamanho)>();
        var p = 0;
        while (p < dados.Length)
        {
            if (!TentarLerElemento(dados, ref p, out var id, out var tamanho))
                break; // fim truncado: fica com o que já leu
            var fim = tamanho is null ? dados.Length : (int)Math.Min((ulong)dados.Length, (ulong)p + tamanho.Value);

            if (Conteineres.Contains(id))
            {
                if (id == IdFaixa) faixas.Add(new Faixa());
                continue; // os filhos vêm em seguida
            }
            if (tamanho is null)
                throw Invalido("elemento de tamanho desconhecido fora de Segment/Cluster");

            var corpo = dados[p..fim];
            var faixa = faixas.Count > 0 ? faixas[^1] : null;
            switch (id)
            {
                case IdNumeroDaFaixa when faixa is not null: faixa.Numero = Inteiro(corpo); break;
                case IdCodec when faixa is not null: faixa.Codec = System.Text.Encoding.ASCII.GetString(corpo).TrimEnd('\0'); break;
                case IdCodecPrivado when faixa is not null: faixa.CodecPrivado = corpo.ToArray(); break;
                case IdCanais when faixa is not null: faixa.Canais = (int)Inteiro(corpo); break;
                case IdBlocoSimples or IdBloco: blocos.Add(LerBloco(corpo, p)); break;
            }
            p = fim;
        }

        var opus = faixas.FirstOrDefault(f => f.Codec == "A_OPUS")
            ?? throw new UseCaseValidationException("O áudio gravado não é Opus. Grave de novo pelo navegador Chrome, Edge ou Firefox.");
        var cabecalho = opus.CodecPrivado is { Length: >= 19 } privado && privado.AsSpan(0, 8).SequenceEqual("OpusHead"u8)
            ? privado
            : OpusHeadPadrao(opus.Canais);

        var bytes = dados.ToArray();
        var pacotes = blocos
            .Where(b => b.Faixa == opus.Numero)
            .Select(b => (ReadOnlyMemory<byte>)bytes.AsMemory(b.Inicio, b.Tamanho))
            .ToList();
        if (pacotes.Count == 0)
            throw new UseCaseValidationException("O áudio gravado está vazio. Grave de novo.");
        return new FaixaOpus(cabecalho, pacotes);
    }

    /// <summary>Bloco Matroska: número da faixa (vint), timecode (2 bytes), flags; o resto é o pacote.</summary>
    private static (ulong Faixa, int Inicio, int Tamanho) LerBloco(ReadOnlySpan<byte> corpo, int deslocamento)
    {
        var p = 0;
        var faixa = LerVint(corpo, ref p, manterMarcador: false)
            ?? throw Invalido("bloco sem número de faixa");
        p += 2;
        if (p >= corpo.Length) throw Invalido("bloco truncado");
        var flags = corpo[p++];
        if ((flags & 0x06) != 0)
            throw new UseCaseValidationException("Este WebM agrupa pacotes (lacing), formato que o EasyStok ainda não converte.");
        return (faixa, deslocamento + p, corpo.Length - p);
    }

    private static bool TentarLerElemento(ReadOnlySpan<byte> dados, ref int p, out uint id, out ulong? tamanho)
    {
        id = 0;
        tamanho = null;
        var inicio = p;
        var lido = LerVint(dados, ref p, manterMarcador: true);
        if (lido is null || p >= dados.Length) { p = inicio; return false; }
        id = (uint)lido.Value;
        var tamanhoInicio = p;
        var comprimento = ComprimentoVint(dados[p]);
        if (comprimento == 0 || p + comprimento > dados.Length) { p = inicio; return false; }
        var desconhecido = true;
        ulong valor = (ulong)(dados[p] & (0xFF >> comprimento));
        if (valor != (ulong)(0xFF >> comprimento)) desconhecido = false;
        for (var i = 1; i < comprimento; i++)
        {
            valor = (valor << 8) | dados[tamanhoInicio + i];
            if (dados[tamanhoInicio + i] != 0xFF) desconhecido = false;
        }
        p = tamanhoInicio + comprimento;
        tamanho = desconhecido ? null : valor;
        return true;
    }

    private static ulong? LerVint(ReadOnlySpan<byte> dados, ref int p, bool manterMarcador)
    {
        if (p >= dados.Length) return null;
        var comprimento = ComprimentoVint(dados[p]);
        if (comprimento == 0 || p + comprimento > dados.Length) return null;
        ulong valor = manterMarcador ? dados[p] : (ulong)(dados[p] & (0xFF >> comprimento));
        for (var i = 1; i < comprimento; i++) valor = (valor << 8) | dados[p + i];
        p += comprimento;
        return valor;
    }

    private static int ComprimentoVint(byte primeiro)
    {
        for (var i = 0; i < 8; i++)
            if ((primeiro & (0x80 >> i)) != 0) return i + 1;
        return 0;
    }

    private static ulong Inteiro(ReadOnlySpan<byte> corpo)
    {
        ulong valor = 0;
        foreach (var b in corpo) valor = (valor << 8) | b;
        return valor;
    }

    /// <summary>OpusHead (RFC 7845 §5.1) para WebM que não trouxe o CodecPrivate: 48 kHz, sem pre-skip.</summary>
    private static byte[] OpusHeadPadrao(int canais)
    {
        var cabecalho = new byte[19];
        "OpusHead"u8.CopyTo(cabecalho);
        cabecalho[8] = 1;
        cabecalho[9] = (byte)Math.Clamp(canais, 1, 2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(cabecalho.AsSpan(12), 48000u);
        return cabecalho;
    }

    private static UseCaseValidationException Invalido(string detalhe) =>
        new($"Não foi possível ler o áudio gravado ({detalhe}). Grave de novo.");
}
