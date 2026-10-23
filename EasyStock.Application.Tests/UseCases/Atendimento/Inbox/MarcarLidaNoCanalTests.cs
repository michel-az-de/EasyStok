using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Inbox;

/// <summary>
/// #1765-6: o cliente vê os dois tiques azuis quando a atendente abre a conversa, não quando a mensagem chega. O
/// <c>marcar-lida</c> do console avisa o canal com a última entrada (a Meta dá como lidas as anteriores junto).
/// </summary>
public class MarcarLidaNoCanalTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly IConversaRepository _repositorio = Substitute.For<IConversaRepository>().EspelharLeituraComLock();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>().SetupExecuteInTransactionSemRetry<ConversaSituacaoResult>();
    private readonly ICanalMensageria _canal = Substitute.For<ICanalMensageria>();

    public MarcarLidaNoCanalTests() => _canal.Canal.Returns(CanalConversa.WhatsApp);

    private GerenciarConversaAtendimentoUseCase UseCase() =>
        new(_repositorio, _uow, resolvedorCanal: new ResolvedorCanal([_canal]),
            logger: NullLogger<GerenciarConversaAtendimentoUseCase>.Instance);

    private Conversa ConversaComEntradas(params (string? Wamid, DirecaoMensagem Direcao)[] mensagens)
    {
        var inicio = DateTime.UtcNow.AddMinutes(-10);
        var conversa = Conversa.Abrir(_empresa, "5511912345678", inicio);
        var lista = new List<Mensagem>();
        for (var i = 0; i < mensagens.Length; i++)
        {
            var (wamid, direcao) = mensagens[i];
            var em = inicio.AddMinutes(i);
            if (direcao == DirecaoMensagem.Entrada)
            {
                conversa.RegistrarEntrada(em);
                lista.Add(Mensagem.Entrada(_empresa, conversa.Id, em, TipoConteudoMensagem.Texto, "oi", wamid));
            }
            else
                lista.Add(Mensagem.Saida(_empresa, conversa.Id, AutorMensagem.Dona, em, TipoConteudoMensagem.Texto, "já vou", wamid));
        }
        _repositorio.ObterPorIdAsync(_empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        _repositorio.ListarMensagensAsync(_empresa, conversa.Id, null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(lista);
        return conversa;
    }

    [Fact]
    public async Task AbrirConversaComNaoLidasMarcaAUltimaEntradaNoCanal()
    {
        var conversa = ConversaComEntradas(("wamid.1", DirecaoMensagem.Entrada), ("wamid.s", DirecaoMensagem.Saida),
            ("wamid.2", DirecaoMensagem.Entrada));

        await UseCase().MarcarLidaAsync(new AcaoConversaCommand(_empresa, Guid.NewGuid(), conversa.Id));

        conversa.NaoLidas.Should().Be(0);
        await _canal.Received(1).MarcarComoLidaAsync("wamid.2", Arg.Any<CancellationToken>());
        // A última entrada é lida sob a trava, antes do commit: a que chegar depois segue não lida no canal também.
        Received.InOrder(() =>
        {
            _repositorio.ListarMensagensAsync(_empresa, conversa.Id, null, Arg.Any<int>(), Arg.Any<CancellationToken>());
            _uow.CommitAsync();
            _canal.MarcarComoLidaAsync("wamid.2", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ConversaJaLidaNaoChamaOCanalDeNovo()
    {
        var conversa = ConversaComEntradas(("wamid.1", DirecaoMensagem.Entrada));
        conversa.MarcarLida();

        await UseCase().MarcarLidaAsync(new AcaoConversaCommand(_empresa, Guid.NewGuid(), conversa.Id));

        await _canal.DidNotReceiveWithAnyArgs().MarcarComoLidaAsync(default!, default);
    }

    [Fact]
    public async Task FalhaDoCanalNaoDesfazOMarcarLida()
    {
        var conversa = ConversaComEntradas(("wamid.1", DirecaoMensagem.Entrada));
        _canal.MarcarComoLidaAsync("wamid.1", Arg.Any<CancellationToken>()).Returns(Task.FromException(new HttpRequestException("fora")));

        var resultado = await UseCase().MarcarLidaAsync(new AcaoConversaCommand(_empresa, Guid.NewGuid(), conversa.Id));

        resultado.Should().NotBeNull();
        conversa.NaoLidas.Should().Be(0);
        await _uow.Received(1).CommitAsync();
    }
}
