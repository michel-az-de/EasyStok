using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Cliente.Foto;

namespace EasyStock.Application.Tests.UseCases;

public sealed class ClienteFotoUseCaseTests
{
    private readonly Guid empresa = Guid.NewGuid();
    private readonly IClienteRepository repo = Substitute.For<IClienteRepository>();
    private readonly IFileStorage storage = Substitute.For<IFileStorage>();
    private readonly IImageProcessor images = Substitute.For<IImageProcessor>();
    private readonly IUnitOfWork uow = Substitute.For<IUnitOfWork>();
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1];

    private ClienteFotoUseCase Criar() => new(repo, storage, images, uow);
    private Cliente Preparar()
    {
        var cliente = Cliente.Criar(empresa, "Cliente de teste");
        repo.GetByIdAsync(empresa, cliente.Id).Returns(cliente);
        images.Optimize(Arg.Any<byte[]>(), Arg.Any<string>(), 512, 85, true).Returns((Jpeg.ToArray(), "image/jpeg", ".jpg"));
        storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<FileUploadRequest>();
            return new StoredFileResult(request.BucketPath + "/" + request.FileName, "/files/nao-usar", request.ContentType, request.Content.Length);
        });
        return cliente;
    }

    [Fact]
    public async Task TrocarFoto_ArmazenaPrivadoEPersisteRotaAutenticada()
    {
        var cliente = Preparar();
        var result = await Criar().EnviarAsync(empresa, cliente.Id, Guid.NewGuid(), "retrato.jpg", "image/jpeg", Jpeg);
        result!.FotoUrl.Should().StartWith($"/api/clientes/{cliente.Id}/foto?v=");
        result.FotoUrl.Should().NotContain("/files/");
        await storage.Received(1).UploadAsync(Arg.Is<FileUploadRequest>(r => !r.IsPublic && r.BucketPath == $"clientes/{empresa}/{cliente.Id}"), Arg.Any<CancellationToken>());
        cliente.FotoChave.Should().StartWith($"clientes/{empresa}/{cliente.Id}/");
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task OutroTenant_NaoLeNemGravaNemRemoveArquivo()
    {
        var cliente = Preparar();
        var outro = Guid.NewGuid();
        var service = Criar();
        (await service.EnviarAsync(outro, cliente.Id, null, "foto.jpg", "image/jpeg", Jpeg)).Should().BeNull();
        (await service.ObterAsync(outro, cliente.Id)).Should().BeNull();
        (await service.RemoverAsync(outro, cliente.Id, null)).Should().BeNull();
        await storage.DidNotReceive().UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>());
        await storage.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/pdf")]
    public async Task MimeNaoImagem_NaoChegaAoStorage(string mime)
    {
        var cliente = Preparar();
        var action = () => Criar().EnviarAsync(empresa, cliente.Id, null, "foto.jpg", mime, Jpeg);
        await action.Should().ThrowAsync<UseCaseValidationException>();
        await storage.DidNotReceive().UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BytesFalsosOuArquivoGrande_SaoRecusadosAntesDeGravar()
    {
        var cliente = Preparar();
        var service = Criar();
        var falso = () => service.EnviarAsync(empresa, cliente.Id, null, "foto.jpg", "image/jpeg", [1, 2, 3]);
        await falso.Should().ThrowAsync<UseCaseValidationException>();
        var grande = () => service.EnviarAsync(empresa, cliente.Id, null, "foto.jpg", "image/jpeg", new byte[ClienteFotoUseCase.TamanhoMaximo + 1]);
        await grande.Should().ThrowAsync<UseCaseValidationException>();
        await storage.DidNotReceive().UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImagemInvalidaQueProcessadorDevolveOriginal_NaoEArmazenada()
    {
        var cliente = Preparar();
        images.Optimize(Jpeg, "image/jpeg", 512, 85, true).Returns((Jpeg, "image/jpeg", ".jpg"));
        var action = () => Criar().EnviarAsync(empresa, cliente.Id, null, "foto.jpg", "image/jpeg", Jpeg);
        await action.Should().ThrowAsync<UseCaseValidationException>();
        await storage.DidNotReceive().UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrocaERemocao_ConservamArquivoAnteriorAteCommit()
    {
        var cliente = Preparar();
        var anterior = $"clientes/{empresa}/{cliente.Id}/{Guid.NewGuid():N}.jpg";
        cliente.FotoChave = anterior;
        var service = Criar();
        await service.EnviarAsync(empresa, cliente.Id, null, "foto.jpg", "image/jpeg", Jpeg);
        await storage.Received(1).DeleteAsync(anterior, Arg.Any<CancellationToken>());
        var atual = cliente.FotoChave!;
        storage.DownloadAsync(atual, Arg.Any<CancellationToken>()).Returns(Jpeg);
        storage.ExistsAsync(atual, Arg.Any<CancellationToken>()).Returns(true);
        (await service.ObterAsync(empresa, cliente.Id)).Should().Equal(Jpeg);
        (await service.RemoverAsync(empresa, cliente.Id, null))!.FotoUrl.Should().BeNull();
        cliente.FotoChave.Should().BeNull();
        await storage.Received(1).DeleteAsync(atual, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChaveCorrompidaDeOutroTenant_NaoEConsultadaNemApagada()
    {
        var cliente = Preparar();
        var estrangeira = $"clientes/{Guid.NewGuid()}/{cliente.Id}/{Guid.NewGuid():N}.jpg";
        cliente.FotoChave = estrangeira;
        var service = Criar();
        (await service.ObterAsync(empresa, cliente.Id)).Should().BeNull();
        await service.RemoverAsync(empresa, cliente.Id, null);
        await storage.DidNotReceive().DownloadAsync(estrangeira, Arg.Any<CancellationToken>());
        await storage.DidNotReceive().DeleteAsync(estrangeira, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChaveComTraversalNaoPodeUsarPrefixoDoClienteParaLerOutroArquivo()
    {
        var cliente = Preparar();
        cliente.FotoChave = $"clientes/{empresa}/{cliente.Id}/..\\..\\outra-empresa\\{Guid.NewGuid():N}.jpg";
        (await Criar().ObterAsync(empresa, cliente.Id)).Should().BeNull();
        ClienteFotoUseCase.Url(cliente).Should().BeNull();
        await Criar().RemoverAsync(empresa, cliente.Id, null);
        await storage.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalhaNoCommit_RemoveSomenteArquivoNovo()
    {
        var cliente = Preparar();
        var anterior = $"clientes/{empresa}/{cliente.Id}/{Guid.NewGuid():N}.jpg";
        cliente.FotoChave = anterior;
        uow.CommitAsync().Returns(Task.FromException<int>(new InvalidOperationException("Banco indisponível")));
        var action = () => Criar().EnviarAsync(empresa, cliente.Id, null, "foto.jpg", "image/jpeg", Jpeg);
        await action.Should().ThrowAsync<InvalidOperationException>();
        await storage.DidNotReceive().DeleteAsync(anterior, Arg.Any<CancellationToken>());
        await storage.Received(1).DeleteAsync(Arg.Is<string>(k => k != anterior), Arg.Any<CancellationToken>());
    }
}
