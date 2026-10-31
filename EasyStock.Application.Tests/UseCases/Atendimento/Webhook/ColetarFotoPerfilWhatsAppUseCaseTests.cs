using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Application.UseCases.Cliente.Foto;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Webhook;

/// <summary>#2155: a foto do perfil do WhatsApp vira a foto do cliente que ainda não tem nenhuma.</summary>
public sealed class ColetarFotoPerfilWhatsAppUseCaseTests
{
    private const string Contato = "5541988887777";
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1];
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IWhatsAppCloudClient _whatsApp = Substitute.For<IWhatsAppCloudClient>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IImageProcessor _images = Substitute.For<IImageProcessor>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();

    public ColetarFotoPerfilWhatsAppUseCaseTests()
    {
        _images.Optimize(Arg.Any<byte[]>(), Arg.Any<string>(), 512, 85, true).Returns((Jpeg.ToArray(), "image/jpeg", ".jpg"));
        _storage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<FileUploadRequest>();
            return new StoredFileResult(request.BucketPath + "/" + request.FileName, "/files/x", request.ContentType, request.Content.Length);
        });
    }

    private ColetarFotoPerfilWhatsAppUseCase Criar() => new(_clientes, _whatsApp,
        new ClienteFotoUseCase(_clientes, _storage, _images, _uow), _tenant, NullLogger<ColetarFotoPerfilWhatsAppUseCase>.Instance);

    private Cliente Cliente(string? fotoChave = null)
    {
        var cliente = EasyStock.Domain.Entities.Cliente.Criar(_empresaId, "Maria");
        cliente.FotoChave = fotoChave;
        _clientes.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);
        return cliente;
    }

    [Fact]
    public async Task ClienteSemFotoGanhaAFotoDoPerfilComHistoricoDoWhatsApp()
    {
        var cliente = Cliente();
        _whatsApp.ObterFotoPerfilAsync(Contato, Arg.Any<CancellationToken>()).Returns(new FotoPerfilWhatsApp(Jpeg, "image/jpeg"));

        await Criar().ExecuteAsync(new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato));

        _tenant.Received(1).SetCurrentTenant(_empresaId);
        cliente.FotoChave.Should().StartWith($"clientes/{_empresaId}/{cliente.Id}/");
        await _storage.Received(1).UploadAsync(Arg.Is<FileUploadRequest>(r => !r.IsPublic), Arg.Any<CancellationToken>());
        await _clientes.Received(1).AddAlteracaoAsync(Arg.Is<ClienteAlteracao>(a => a.Campo == "Foto" && a.Origem == "whatsapp"));
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task FotoEscolhidaPelaLojaNaoETrocada()
    {
        var cliente = Cliente($"clientes/{_empresaId}/x/{Guid.NewGuid():N}.jpg");

        await Criar().ExecuteAsync(new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato));

        await _whatsApp.DidNotReceiveWithAnyArgs().ObterFotoPerfilAsync(default!, default);
        await _storage.DidNotReceiveWithAnyArgs().UploadAsync(default!, default);
    }

    [Fact]
    public async Task SemFotoNoPerfilNadaMuda()
    {
        var cliente = Cliente();
        _whatsApp.ObterFotoPerfilAsync(Contato, Arg.Any<CancellationToken>()).Returns((FotoPerfilWhatsApp?)null);

        await Criar().ExecuteAsync(new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato));

        cliente.FotoChave.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task FalhaDaPonteNaoPropaga()
    {
        var cliente = Cliente();
        _whatsApp.ObterFotoPerfilAsync(Contato, Arg.Any<CancellationToken>())
            .Returns<FotoPerfilWhatsApp?>(_ => throw new WhatsAppCloudException(0, "ponte fora", ehPermanente: false));

        var executar = () => Criar().ExecuteAsync(new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato));

        await executar.Should().NotThrowAsync();
        cliente.FotoChave.Should().BeNull();
    }

    [Fact]
    public async Task ArquivoQueNaoEImagemERecusadoSemQuebrar()
    {
        var cliente = Cliente();
        _whatsApp.ObterFotoPerfilAsync(Contato, Arg.Any<CancellationToken>()).Returns(new FotoPerfilWhatsApp([1, 2, 3], "image/gif"));

        var executar = () => Criar().ExecuteAsync(new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato));

        await executar.Should().NotThrowAsync();
        cliente.FotoChave.Should().BeNull();
    }
}
