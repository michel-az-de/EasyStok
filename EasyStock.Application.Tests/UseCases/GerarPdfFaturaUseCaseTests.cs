using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Faturas.GerarPdfFatura;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Cobertura para o UseCase de geracao de PDF (F4). Foco: cache hit/miss,
/// 404 quando fatura nao existe, atualiza PdfStorageKey apos render.
/// </summary>
public class GerarPdfFaturaUseCaseTests
{
    private readonly IFaturaRepository _repo = Substitute.For<IFaturaRepository>();
    private readonly IFaturaPdfRenderer _renderer = Substitute.For<IFaturaPdfRenderer>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static Fatura NovaFaturaEmitida(Guid empresaId, decimal total = 100m, string? pdfStorageKey = null)
    {
        var f = Fatura.Criar(
            empresaId, "2026-000123",
            new DadosFaturado("Cliente"), new DadosEmissor("Emissor"),
            OrigemFatura.Avulsa, DateTime.UtcNow, DateTime.UtcNow.AddDays(7));
        f.AdicionarItem("Servico", 1, total);
        f.Emitir();
        f.PdfStorageKey = pdfStorageKey;
        return f;
    }

    private GerarPdfFaturaUseCase CreateUseCase() =>
        new(_repo, _renderer, _storage, _uow, Substitute.For<ILogger<GerarPdfFaturaUseCase>>());

    [Fact]
    public async Task RetornaNull_QuandoFaturaNaoEncontrada_Cliente()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Fatura?)null);

        var result = await CreateUseCase()
            .ExecuteAsync(new GerarPdfFaturaCommand(Guid.NewGuid(), Guid.NewGuid(), Admin: false));

        result.Should().BeNull();
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, default);
    }

    [Fact]
    public async Task RetornaNull_QuandoFaturaNaoEncontrada_Admin()
    {
        _repo.GetByIdAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Fatura?)null);

        var result = await CreateUseCase()
            .ExecuteAsync(new GerarPdfFaturaCommand(EmpresaId: null, FaturaId: Guid.NewGuid(), Admin: true));

        result.Should().BeNull();
    }

    [Fact]
    public async Task CacheMiss_RenderizaUploadAtualizaStorageKeyEPersiste()
    {
        var empresaId = Guid.NewGuid();
        var fatura = NovaFaturaEmitida(empresaId, pdfStorageKey: null);
        _repo.GetByIdAsync(empresaId, fatura.Id, Arg.Any<CancellationToken>()).Returns(fatura);

        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // "%PDF"
        _renderer.RenderAsync(fatura, Arg.Any<CancellationToken>()).Returns(pdfBytes);

        _storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFileResult(
                StorageKey: $"faturas/{empresaId:N}/{fatura.Id:N}.pdf",
                Url: "https://example.com/pdf",
                ContentType: "application/pdf",
                Size: pdfBytes.Length));

        var result = await CreateUseCase()
            .ExecuteAsync(new GerarPdfFaturaCommand(empresaId, fatura.Id, Admin: false));

        result.Should().NotBeNull();
        result!.Bytes.Should().BeEquivalentTo(pdfBytes);
        result.ContentType.Should().Be("application/pdf");
        result.FileName.Should().Be($"fatura-{fatura.Numero}.pdf");
        result.VeioDoCache.Should().BeFalse();

        fatura.PdfStorageKey.Should().Be(result.StorageKey);
        await _repo.Received(1).UpdateAsync(fatura, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task CacheHit_DownloadDoStorageEPulaRender()
    {
        var empresaId = Guid.NewGuid();
        var key = $"faturas/{empresaId:N}/abc.pdf";
        var fatura = NovaFaturaEmitida(empresaId, pdfStorageKey: key);
        _repo.GetByIdAsync(empresaId, fatura.Id, Arg.Any<CancellationToken>()).Returns(fatura);

        var cached = new byte[] { 0x25, 0x50 };
        _storage.DownloadAsync(key, Arg.Any<CancellationToken>()).Returns(cached);

        var result = await CreateUseCase()
            .ExecuteAsync(new GerarPdfFaturaCommand(empresaId, fatura.Id, Admin: false));

        result.Should().NotBeNull();
        result!.VeioDoCache.Should().BeTrue();
        result.Bytes.Should().BeEquivalentTo(cached);
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, default);
        await _storage.DidNotReceiveWithAnyArgs().UploadAsync(default!, default);
    }

    [Fact]
    public async Task CacheCorrompido_RegeneraSilenciosamente()
    {
        var empresaId = Guid.NewGuid();
        var key = $"faturas/{empresaId:N}/orphan.pdf";
        var fatura = NovaFaturaEmitida(empresaId, pdfStorageKey: key);
        _repo.GetByIdAsync(empresaId, fatura.Id, Arg.Any<CancellationToken>()).Returns(fatura);

        _storage.DownloadAsync(key, Arg.Any<CancellationToken>())
            .Returns<Task<byte[]>>(_ => throw new InvalidOperationException("S3 KeyNotFound"));

        var pdfBytes = new byte[] { 1, 2, 3 };
        _renderer.RenderAsync(fatura, Arg.Any<CancellationToken>()).Returns(pdfBytes);

        _storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFileResult($"faturas/{empresaId:N}/{fatura.Id:N}.pdf", "u", "application/pdf", 3));

        var result = await CreateUseCase()
            .ExecuteAsync(new GerarPdfFaturaCommand(empresaId, fatura.Id, Admin: false));

        result.Should().NotBeNull();
        result!.VeioDoCache.Should().BeFalse();
        await _renderer.Received(1).RenderAsync(fatura, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForcarRegenerar_IgnoraCacheEReescrevePdfStorageKey()
    {
        var empresaId = Guid.NewGuid();
        var keyAntigo = "faturas/old/foo.pdf";
        var fatura = NovaFaturaEmitida(empresaId, pdfStorageKey: keyAntigo);
        _repo.GetByIdAsync(empresaId, fatura.Id, Arg.Any<CancellationToken>()).Returns(fatura);

        _renderer.RenderAsync(fatura, Arg.Any<CancellationToken>()).Returns(new byte[] { 9, 9 });

        var keyNovo = $"faturas/{empresaId:N}/{fatura.Id:N}.pdf";
        _storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFileResult(keyNovo, "u", "application/pdf", 2));

        var result = await CreateUseCase()
            .ExecuteAsync(new GerarPdfFaturaCommand(empresaId, fatura.Id, Admin: false, ForcarRegenerar: true));

        result.Should().NotBeNull();
        result!.VeioDoCache.Should().BeFalse();
        await _storage.DidNotReceive().DownloadAsync(keyAntigo, Arg.Any<CancellationToken>());
        await _renderer.Received(1).RenderAsync(fatura, Arg.Any<CancellationToken>());
        fatura.PdfStorageKey.Should().Be(keyNovo);
    }

    [Fact]
    public async Task FaturaIdVazio_LancaUseCaseValidationException()
    {
        var act = () => CreateUseCase()
            .ExecuteAsync(new GerarPdfFaturaCommand(Guid.NewGuid(), Guid.Empty));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
