using System.Security.Cryptography;
using System.Text;
using EasyStock.Infra.Async.Reporting;
using FluentAssertions;

namespace EasyStock.Infra.Async.UnitTests.Reporting;

/// <summary>#2664: o hash e o tamanho gravados no artefato do relatório saem deste decorador.</summary>
public class HashingCountingStreamTests
{
    private static string Sha256(byte[] dados) => Convert.ToHexString(SHA256.HashData(dados)).ToLowerInvariant();

    [Fact]
    public async Task Hash_e_tamanho_conferem_com_o_que_chegou_ao_destino_por_todas_as_escritas()
    {
        var destino = new MemoryStream();
        var stream = new HashingCountingStream(destino);
        byte[] a = [1, 2, 3], b = [4, 5], c = [6], d = [7, 8, 9, 10];

        stream.Write(a, 0, a.Length);
        stream.Write(b.AsSpan());
        await stream.WriteAsync(c, 0, c.Length, CancellationToken.None);
        await stream.WriteAsync(d.AsMemory());
        stream.Flush();
        await stream.FlushAsync(CancellationToken.None);

        var esperado = destino.ToArray();
        esperado.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        stream.BytesWritten.Should().Be(10);
        stream.GetHexHash().Should().Be(Sha256(esperado)).And.HaveLength(64);
        stream.GetHexHash().Should().Be(Sha256(esperado), "pode ser lido mais de uma vez");
        stream.Length.Should().Be(10);
        stream.Position.Should().Be(10);
    }

    [Fact]
    public void Sem_escrita_o_hash_e_o_do_vazio()
    {
        using var stream = new HashingCountingStream(new MemoryStream());

        stream.GetHexHash().Should().Be(Sha256([]));
        stream.BytesWritten.Should().Be(0);
    }

    [Fact]
    public void E_so_de_escrita()
    {
        using var stream = new HashingCountingStream(new MemoryStream());

        stream.CanRead.Should().BeFalse();
        stream.CanSeek.Should().BeFalse();
        stream.CanWrite.Should().BeTrue();
        FluentActions.Invoking(() => stream.Read(new byte[1], 0, 1)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => stream.Seek(0, SeekOrigin.Begin)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => stream.SetLength(1)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => stream.Position = 0).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Destino_nulo_e_recusado()
    {
        FluentActions.Invoking(() => new HashingCountingStream(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Fechar_fecha_o_destino_uma_vez_so()
    {
        var destino = new MemoryStream();
        var stream = new HashingCountingStream(destino);
        await stream.WriteAsync(Encoding.UTF8.GetBytes("x"));

        await stream.DisposeAsync();
        await stream.DisposeAsync();

        destino.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void Dispose_sincrono_fecha_o_destino_e_e_idempotente()
    {
        var destino = new MemoryStream();
        var stream = new HashingCountingStream(destino);

        stream.Dispose();
        stream.Dispose();

        destino.CanWrite.Should().BeFalse();
    }
}
