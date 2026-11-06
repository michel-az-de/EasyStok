using System.Text.Json;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Notifications.Anexos;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Notifications.Anexos;

/// <summary>#2211: o metadado <c>anexo</c> do template vira a URL do arquivo gerado.</summary>
public class AnexadorMensagemTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IModeloAnexoNotificacao _modelo = Substitute.For<IModeloAnexoNotificacao>();
    private readonly Dictionary<string, object?> _vars = new() { ["pedidoId"] = Guid.NewGuid().ToString() };

    public AnexadorMensagemTests()
    {
        _modelo.Codigo.Returns("canhoto");
        _modelo.GerarAsync(_empresaId, Arg.Any<IDictionary<string, object?>>(), Arg.Any<FormatoAnexo>(), Arg.Any<CancellationToken>())
            .Returns(ci => new AnexoGerado([1, 2, 3],
                ci.ArgAt<FormatoAnexo>(2) == FormatoAnexo.Pdf ? "application/pdf" : "image/png",
                ci.ArgAt<FormatoAnexo>(2) == FormatoAnexo.Pdf ? "pdf" : "png"));
        _storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new StoredFileResult("k", $"https://cdn.test/{ci.Arg<FileUploadRequest>().FileName}", "x", 3));
    }

    private AnexadorMensagem Criar() => new([_modelo], _storage, NullLogger<AnexadorMensagem>.Instance);

    private EventoNotificacao Evento() =>
        EventoNotificacao.Criar(TipoEventoNotificacao.AvisoEquipePedidoComCanhoto, _empresaId, "{}");

    private static Dictionary<string, string> Ler(string? json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json!)!;

    [Fact]
    public async Task PngViraImagemPublicaESomeAsChavesDoAnexo()
    {
        var r = Ler(await Criar().AnexarAsync(Evento(), """{"template":"aviso","anexo":"canhoto","anexoFormato":"png"}""", _vars));

        r.Should().ContainKey("imagem").WhoseValue.Should().StartWith("https://cdn.test/").And.EndWith(".png");
        r.Should().NotContainKeys("anexo", "anexoFormato");
        r["template"].Should().Be("aviso");
        await _storage.Received(1).UploadAsync(
            Arg.Is<FileUploadRequest>(u => u.IsPublic && u.BucketPath.Contains(_empresaId.ToString("N")) && u.ContentType == "image/png"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PdfViraDocumento()
    {
        var r = Ler(await Criar().AnexarAsync(Evento(), """{"anexo":"canhoto","anexoFormato":"pdf"}""", _vars));

        r.Should().ContainKey("documento").WhoseValue.Should().EndWith(".pdf");
        r.Should().NotContainKey("imagem");
    }

    [Fact]
    public async Task SemAnexoDevolveOsMetadadosComoVieram()
    {
        const string metadados = """{"template":"aviso"}""";

        (await Criar().AnexarAsync(Evento(), metadados, _vars)).Should().Be(metadados);
        await _storage.DidNotReceiveWithAnyArgs().UploadAsync(default!, default);
    }

    [Fact]
    public async Task MesmoEventoGeraUmaVezParaTodaAAudiencia()
    {
        var sut = Criar();
        var evento = Evento();

        var a = Ler(await sut.AnexarAsync(evento, """{"anexo":"canhoto"}""", _vars));
        var b = Ler(await sut.AnexarAsync(evento, """{"anexo":"canhoto"}""", _vars));

        b["imagem"].Should().Be(a["imagem"]);
        await _storage.ReceivedWithAnyArgs(1).UploadAsync(default!, default);
    }

    [Fact]
    public async Task FalhaNaGeracaoMandaOTextoSemAnexo()
    {
        _modelo.GerarAsync(default, default!, default, default).ReturnsForAnyArgs<AnexoGerado?>(_ => throw new InvalidOperationException("x"));

        var r = Ler(await Criar().AnexarAsync(Evento(), """{"template":"aviso","anexo":"canhoto"}""", _vars));

        r.Should().ContainKey("template").And.NotContainKeys("imagem", "anexo");
    }

    [Fact]
    public async Task ModeloNovoEntraSoPeloCatalogoSemMexerNoMotor()
    {
        var novo = Substitute.For<IModeloAnexoNotificacao>();
        novo.Codigo.Returns("etiqueta");
        novo.GerarAsync(_empresaId, Arg.Any<IDictionary<string, object?>>(), FormatoAnexo.Png, Arg.Any<CancellationToken>())
            .Returns(new AnexoGerado([9], "image/png", "png"));
        var sut = new AnexadorMensagem([_modelo, novo], _storage, NullLogger<AnexadorMensagem>.Instance);

        var r = Ler(await sut.AnexarAsync(Evento(), """{"anexo":"etiqueta"}""", _vars));

        r.Should().ContainKey("imagem").WhoseValue.Should().EndWith(".png");
        await novo.Received(1).GerarAsync(_empresaId, Arg.Any<IDictionary<string, object?>>(), FormatoAnexo.Png, Arg.Any<CancellationToken>());
        await _modelo.DidNotReceiveWithAnyArgs().GerarAsync(default, default!, default, default);
    }

    [Fact]
    public async Task ModeloDesconhecidoMandaOTextoSemAnexo()
    {
        var r = Ler(await Criar().AnexarAsync(Evento(), """{"anexo":"inexistente"}""", _vars));

        r.Should().BeEmpty();
    }
}
