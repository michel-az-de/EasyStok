using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications.Anexos;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// O WhatsApp baixa o documento por URL pública. O link assinado do armazenamento local (<c>/api/files/local-signed/...</c>)
/// não existe na API e devolvia 404: o PDF do pedido e o comprovante sobem com nome aleatório público, como o canhoto.
/// </summary>
public class PublicadorDocumentoPedidoTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly string Codigo = "4AA2E393";

    private static readonly ResumoPedidoDto Resumo = new(
        "Casa da Baba", Codigo, [new ResumoPedidoItemDto("Espaguete", 1m, 19m, null)], 0m, 19m, "sábado", null);

    private static readonly PixPdfDto Pix = new("00020126CODIGOFICTICIO", 19m, 0m, "Thatiane");

    private readonly IRenderizadorPdfPedido _renderizador = Substitute.For<IRenderizadorPdfPedido>();
    private readonly IFileStorage _armazenamento = Substitute.For<IFileStorage>();
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["App:BaseUrl"] = "https://loja.exemplo",
    }).Build();

    public PublicadorDocumentoPedidoTests()
    {
        _renderizador.RenderizarPedidoAsync(Arg.Any<ResumoPedidoDto>(), Arg.Any<PixPdfDto>(), Arg.Any<CancellationToken>())
            .Returns(new AnexoGerado([37, 80, 68, 70], "application/pdf", "pdf"));
        _renderizador.RenderizarComprovanteAsync(Arg.Any<ResumoPedidoDto>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new AnexoGerado([37, 80, 68, 70], "application/pdf", "pdf"));
        _armazenamento.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFileResult("k", "/files/atendimento/documentos/abc/xyz.pdf", "application/pdf", 4));
        // Como o LocalFileStorage de produção: a rota local-signed não existe na API (404 no WhatsApp).
        _armazenamento.CreatePreSignedDownloadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Uri("https://loja.exemplo/api/files/local-signed/tok"));
    }

    private PublicadorDocumentoPedido Publicador() => new(_renderizador, _armazenamento, _config);

    [Fact]
    public async Task PedidoSobeComNomeAleatorioPublicoENaoUsaLinkAssinado()
    {
        var doc = await Publicador().PublicarPedidoAsync(EmpresaId, Resumo, Pix, default);

        await _armazenamento.Received(1).UploadAsync(
            Arg.Is<FileUploadRequest>(r => r.IsPublic && r.ContentType == "application/pdf" && r.FileName.EndsWith(".pdf")
                && r.FileName != "Pedido-" + Codigo + ".pdf"),
            Arg.Any<CancellationToken>());
        await _armazenamento.DidNotReceiveWithAnyArgs().CreatePreSignedDownloadUrlAsync(default!, default, default!, default);
        doc.Url.Should().Be("https://loja.exemplo/files/atendimento/documentos/abc/xyz.pdf");
        doc.NomeArquivo.Should().Be("Pedido-" + Codigo + ".pdf");
    }

    [Fact]
    public async Task ComprovanteSobeComNomeAleatorioPublicoENaoUsaLinkAssinado()
    {
        var doc = await Publicador().PublicarComprovanteAsync(EmpresaId, Resumo, DateTime.UtcNow, default);

        await _armazenamento.Received(1).UploadAsync(
            Arg.Is<FileUploadRequest>(r => r.IsPublic && r.ContentType == "application/pdf"), Arg.Any<CancellationToken>());
        await _armazenamento.DidNotReceiveWithAnyArgs().CreatePreSignedDownloadUrlAsync(default!, default, default!, default);
        doc.Url.Should().StartWith("https://loja.exemplo/files/");
        doc.NomeArquivo.Should().Be("Comprovante-" + Codigo + ".pdf");
    }

    [Fact]
    public async Task UploadSemUrlHttpsFalha_ComoAntes()
    {
        _armazenamento.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFileResult("k", "http://inseguro/x.pdf", "application/pdf", 4));
        var config = new ConfigurationBuilder().Build();

        var acao = () => new PublicadorDocumentoPedido(_renderizador, _armazenamento, config).PublicarPedidoAsync(EmpresaId, Resumo, Pix, default);

        await acao.Should().ThrowAsync<InvalidOperationException>();
    }
}
