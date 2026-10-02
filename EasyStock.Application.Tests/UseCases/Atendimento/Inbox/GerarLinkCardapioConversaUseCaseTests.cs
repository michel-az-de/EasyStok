using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Tests.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Inbox;

/// <summary>
/// #1353: "Enviar cardápio" do console pede à API o mesmo link da loja que o agente usa (S48),
/// em vez de montar um link para a tela do próprio console.
/// </summary>
public class GerarLinkCardapioConversaUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly LinkCardapioConversaServiceTests.RepositorioEmMemoria _links = new();
    private readonly GerarLinkCardapioConversaUseCase _useCase;

    public GerarLinkCardapioConversaUseCaseTests()
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://casadababa.com" })
            .Build();
        var servico = new LinkCardapioConversaService(_links,
            new SaudacaoAtendimento(Substitute.For<IStorefrontRepository>(), configuracao));
        _useCase = new GerarLinkCardapioConversaUseCase(_conversas, servico, _unitOfWork, new FakeTimeProvider(Agora));
    }

    [Fact]
    public async Task ConversaDaEmpresa_GravaODevolveOLinkDaLojaComToken()
    {
        var conversa = Conversa.Abrir(EmpresaId, "5511999998888", Agora, "Maria");
        _conversas.ObterPorIdAsync(EmpresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        var link = await _useCase.ExecuteAsync(EmpresaId, conversa.Id);

        link.Url.Should().StartWith("https://casadababa.com/cardapio?c=");
        link.ExpiraEm.Should().Be(Agora.AddHours(24));
        _links.Links.Should().ContainSingle(l => l.ConversaId == conversa.Id && l.EmpresaId == EmpresaId);
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ConversaDeOutraEmpresa_NaoGravaLink()
    {
        var id = Guid.NewGuid();
        _conversas.ObterPorIdAsync(EmpresaId, id, Arg.Any<CancellationToken>()).Returns((Conversa?)null);

        var acao = () => _useCase.ExecuteAsync(EmpresaId, id);

        await acao.Should().ThrowAsync<ConversaNaoEncontradaException>();
        _links.Links.Should().BeEmpty();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }
}
