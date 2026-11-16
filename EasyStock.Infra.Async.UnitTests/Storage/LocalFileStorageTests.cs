using System.Text;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Infra.Async.Storage;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Infra.Async.UnitTests.Storage;

/// <summary>
/// #2664: <see cref="LocalFileStorage"/> é o storage de produção (<c>FileStorage__Provider: Local</c>) e não tinha teste.
/// Cada teste usa uma pasta própria no TEMP, com a raiz <c>uploads</c> dentro dela.
/// </summary>
public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "easystok-local-storage-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        Directory.CreateDirectory(_base);
        _storage = Criar(new FileStorageOptions { LocalRootPath = Path.Combine(_base, "uploads"), PublicBaseUrl = "/files/" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_base))
            Directory.Delete(_base, recursive: true);
    }

    private LocalFileStorage Criar(FileStorageOptions opcoes)
    {
        var ambiente = Substitute.For<IHostEnvironment>();
        ambiente.ContentRootPath.Returns(_base);
        return new LocalFileStorage(Options.Create(opcoes), ambiente);
    }

    private static FileUploadRequest Png(string pasta, string nome = "foto.png") =>
        new(pasta, nome, "image/png", [1, 2, 3, 4]);

    [Fact]
    public async Task Upload_grava_na_pasta_e_devolve_chave_url_e_tamanho()
    {
        var salvo = await _storage.UploadAsync(Png("lotes/123/"));

        salvo.StorageKey.Should().Be("lotes/123/foto.png");
        salvo.Url.Should().Be("/files/lotes/123/foto.png");
        salvo.Size.Should().Be(4);
        (await _storage.DownloadAsync(salvo.StorageKey)).Should().Equal(1, 2, 3, 4);
        (await _storage.ExistsAsync(salvo.StorageKey, default)).Should().BeTrue();
    }

    [Fact]
    public async Task Upload_fora_da_raiz_e_recusado()
    {
        var act = () => _storage.UploadAsync(Png("../fora"));

        await act.Should().ThrowAsync<ArgumentException>();
        Directory.Exists(Path.Combine(_base, "fora")).Should().BeFalse();
    }

    [Fact]
    public async Task Pasta_irma_com_o_mesmo_prefixo_da_raiz_e_recusada()
    {
        // "uploads-x" começa com "uploads": a trava por StartsWith sem separador deixava passar.
        var upload = () => _storage.UploadAsync(Png("../uploads-x"));
        var download = () => _storage.DownloadAsync("../uploads-x/segredo.txt");

        await upload.Should().ThrowAsync<ArgumentException>();
        await download.Should().ThrowAsync<ArgumentException>();
        Directory.Exists(Path.Combine(_base, "uploads-x")).Should().BeFalse();
        (await _storage.ExistsAsync("../uploads-x/segredo.txt", default)).Should().BeFalse();
        (await _storage.ListAsync("../uploads-x")).Should().BeEmpty();
    }

    [Fact]
    public async Task Caminho_fora_da_raiz_e_recusado_em_toda_operacao()
    {
        await FluentActions.Awaiting(() => _storage.DeleteAsync("../x.txt")).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => _storage.DownloadAsync("../x.txt")).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => _storage.DownloadStreamAsync("../x.txt", default)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => _storage.OpenUploadStreamAsync("../x.txt", "text/csv", default))
            .Should().ThrowAsync<ArgumentException>();
        (await _storage.ExistsAsync("../x.txt", default)).Should().BeFalse();
        (await _storage.ListAsync("../")).Should().BeEmpty();
    }

    [Fact]
    public async Task Lista_os_arquivos_da_pasta_e_exclusao_remove()
    {
        await _storage.UploadAsync(Png("acervo", "a.png"));
        await _storage.UploadAsync(Png("acervo", "b.png"));

        var arquivos = await _storage.ListAsync("/acervo/");

        arquivos.Select(a => a.StorageKey).Should().BeEquivalentTo("acervo/a.png", "acervo/b.png");
        arquivos.Should().OnlyContain(a => a.SizeBytes == 4);

        await _storage.DeleteAsync("acervo/a.png");
        await _storage.DeleteAsync("acervo/nao-existe.png");

        (await _storage.ListAsync("acervo")).Select(a => a.FileName).Should().Equal("b.png");
        (await _storage.ListAsync("pasta-que-nao-existe")).Should().BeEmpty();
    }

    [Fact]
    public async Task Stream_de_upload_so_aparece_com_o_nome_final_depois_de_fechar()
    {
        const string chave = "relatorios/run-1/vendas.csv";
        var stream = await _storage.OpenUploadStreamAsync(chave, "text/csv", default);
        await stream.WriteAsync(Encoding.UTF8.GetBytes("a;b\n1;2\n"));

        (await _storage.ExistsAsync(chave, default)).Should().BeFalse("o arquivo é escrito em .tmp até fechar");

        await stream.DisposeAsync();

        (await _storage.ExistsAsync(chave, default)).Should().BeTrue();
        await using var leitura = await _storage.DownloadStreamAsync(chave, default);
        using var leitor = new StreamReader(leitura);
        (await leitor.ReadToEndAsync()).Should().Be("a;b\n1;2\n");
    }

    [Fact]
    public async Task Exclusao_remove_tambem_o_envio_interrompido_da_chave()
    {
        // #2788: o Worker morre no meio do envio. O SO fecha o arquivo e nada renomeia o envio para a chave.
        const string chave = "relatorios/run-3/vendas.csv";
        var envio = (FileStream)await _storage.OpenUploadStreamAsync(chave, "text/csv", default);
        await envio.WriteAsync(Encoding.UTF8.GetBytes("a;b\n1;2\n"));
        await envio.FlushAsync();
        envio.SafeFileHandle.Dispose();
        GC.SuppressFinalize(envio);

        await _storage.DeleteAsync(chave);

        Directory.GetFiles(_base, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Stream_de_upload_fechado_sincrono_tambem_renomeia()
    {
        const string chave = "relatorios/run-2/estoque.csv";
        var stream = await _storage.OpenUploadStreamAsync(chave, "text/csv", default);
        stream.Write([65]);

        stream.Dispose();

        (await _storage.DownloadAsync(chave)).Should().Equal(65);
    }

    [Fact]
    public async Task Download_em_stream_de_arquivo_ausente_lanca_nao_encontrado()
    {
        var act = () => _storage.DownloadStreamAsync("relatorios/ausente.csv", default);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Url_de_download_leva_a_chave_a_validade_e_o_nome()
    {
        var url = await _storage.CreatePreSignedDownloadUrlAsync("relatorios/r.csv", TimeSpan.FromMinutes(5), "vendas maio.csv", default);

        url.IsAbsoluteUri.Should().BeFalse();
        var partes = url.ToString()["/api/files/local-signed/".Length..].Split('.', 3);
        var base64 = partes[0].Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Should().Be("relatorios/r.csv");
        long.Parse(partes[1]).Should().BeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        partes[2].Should().Be("vendas%20maio.csv");
    }

    [Fact]
    public void Raiz_relativa_fica_dentro_do_content_root()
    {
        Criar(new FileStorageOptions { LocalRootPath = "uploaded-files" }).GetRootPath()
            .Should().Be(Path.Combine(_base, "uploaded-files"));
        _storage.GetRootPath().Should().Be(Path.Combine(_base, "uploads"));
    }
}
