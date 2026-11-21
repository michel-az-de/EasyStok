using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Application.UseCases.Cliente.Foto;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

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
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private JanelaFotoPerfilWhatsApp? _janela;

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
        new ClienteFotoUseCase(_clientes, _storage, _images, _uow), _tenant, _janela ??= new JanelaFotoPerfilWhatsApp(_relogio),
        _relogio, NullLogger<ColetarFotoPerfilWhatsAppUseCase>.Instance);

    private void FotoGravadaPor(Cliente cliente, string origem, TimeSpan ha) =>
        _clientes.GetAlteracoesAsync(cliente.Id, Arg.Any<int>()).Returns(new[]
        {
            new ClienteAlteracao
            {
                Id = Guid.NewGuid(), EmpresaId = _empresaId, ClienteId = cliente.Id, Campo = "Foto",
                ValorNovo = cliente.FotoChave, Origem = origem, AlteradoEm = _relogio.GetUtcNow().UtcDateTime - ha,
            },
        });

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

    // #2838: a foto que veio do WhatsApp envelhece; a escolhida pela loja nunca é trocada.
    [Fact]
    public async Task FotoAntigaDoWhatsAppEAtualizada()
    {
        var cliente = Cliente($"clientes/{_empresaId}/x/{Guid.NewGuid():N}.jpg");
        var antiga = cliente.FotoChave;
        FotoGravadaPor(cliente, "whatsapp", TimeSpan.FromDays(8));
        _whatsApp.ObterFotoPerfilAsync(Contato, Arg.Any<CancellationToken>()).Returns(new FotoPerfilWhatsApp(Jpeg, "image/jpeg"));

        await Criar().ExecuteAsync(new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato));

        cliente.FotoChave.Should().NotBe(antiga).And.StartWith($"clientes/{_empresaId}/{cliente.Id}/");
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task FotoRecenteDoWhatsAppNaoChamaAPonte()
    {
        var cliente = Cliente($"clientes/{_empresaId}/x/{Guid.NewGuid():N}.jpg");
        FotoGravadaPor(cliente, "whatsapp", TimeSpan.FromDays(1));

        await Criar().ExecuteAsync(new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato));

        await _whatsApp.DidNotReceiveWithAnyArgs().ObterFotoPerfilAsync(default!, default);
    }

    [Fact]
    public async Task FotoAntigaEscolhidaPelaLojaNaoETrocada()
    {
        var cliente = Cliente($"clientes/{_empresaId}/x/{Guid.NewGuid():N}.jpg");
        FotoGravadaPor(cliente, "console", TimeSpan.FromDays(30));

        await Criar().ExecuteAsync(new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato));

        await _whatsApp.DidNotReceiveWithAnyArgs().ObterFotoPerfilAsync(default!, default);
    }

    // Cada mensagem enfileira; a ponte é consultada no máximo uma vez por cliente dentro da janela.
    [Fact]
    public async Task MensagensSeguidasConsultamAPonteUmaVezSo()
    {
        var cliente = Cliente();
        _whatsApp.ObterFotoPerfilAsync(Contato, Arg.Any<CancellationToken>()).Returns((FotoPerfilWhatsApp?)null);
        var job = new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato);

        await Criar().ExecuteAsync(job);
        await Criar().ExecuteAsync(job);
        _relogio.Advance(JanelaFotoPerfilWhatsApp.Intervalo + TimeSpan.FromMinutes(1));
        await Criar().ExecuteAsync(job);

        await _whatsApp.Received(2).ObterFotoPerfilAsync(Contato, Arg.Any<CancellationToken>());
    }

    // Revisão da #2838: com a busca em toda mensagem, a foto removida pela loja voltaria na mensagem seguinte.
    [Fact]
    public async Task FotoRemovidaPelaLojaNaoVolta()
    {
        var cliente = Cliente();
        FotoGravadaPor(cliente, "console", TimeSpan.FromDays(1));

        await Criar().ExecuteAsync(new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato));

        await _whatsApp.DidNotReceiveWithAnyArgs().ObterFotoPerfilAsync(default!, default);
    }

    [Fact]
    public async Task DentroDaJanelaNemConsultaOBanco()
    {
        var cliente = Cliente();
        _whatsApp.ObterFotoPerfilAsync(Contato, Arg.Any<CancellationToken>()).Returns((FotoPerfilWhatsApp?)null);
        var job = new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato);
        await Criar().ExecuteAsync(job);
        _clientes.ClearReceivedCalls();

        await Criar().ExecuteAsync(job);

        await _clientes.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task FalhaDaPonteNaoGastaAJanela()
    {
        var cliente = Cliente();
        _whatsApp.ObterFotoPerfilAsync(Contato, Arg.Any<CancellationToken>())
            .Returns<FotoPerfilWhatsApp?>(_ => throw new WhatsAppCloudException(0, "fora do ar", ehPermanente: false), _ => null);
        var job = new ColetarFotoPerfilWhatsAppJob(_empresaId, cliente.Id, Contato);

        await Criar().ExecuteAsync(job);
        await Criar().ExecuteAsync(job);

        await _whatsApp.Received(2).ObterFotoPerfilAsync(Contato, Arg.Any<CancellationToken>());
    }
}
