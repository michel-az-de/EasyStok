using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Inbox;

/// <summary>
/// #1408: a loja inicia a conversa com um cliente cadastrado que nunca escreveu. Sem entrada do cliente não há janela
/// de 24 h, então só sai modelo aprovado; o envio é o mesmo do <see cref="EnviarModeloConversaUseCase"/>.
/// </summary>
public class IniciarConversaClienteUseCaseTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IConsentimentoContatoRepository _consentimentos = Substitute.For<IConsentimentoContatoRepository>();
    private readonly ICatalogoModelosAtendimento _catalogo = Substitute.For<ICatalogoModelosAtendimento>();
    private readonly ICanalMensageria _canal = Substitute.For<ICanalMensageria>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly List<Conversa> _abertas = [];
    private readonly List<Mensagem> _mensagens = [];
    private readonly Cliente _cliente;

    public IniciarConversaClienteUseCaseTests()
    {
        _cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = _empresa, Nome = "Ana Souza", Telefone = "(11) 98888-7777" };
        _clientes.GetByIdAsync(_empresa, _cliente.Id).Returns(_ => _cliente);

        // Banco em memória: a aberta do contato vale para as duas grafias do nono dígito, como no repositório (#1290).
        _conversas.ObterAbertaPorContatoAsync(_empresa, CanalConversa.WhatsApp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(c => _abertas.FirstOrDefault(a => a.EstaAberta
                && NormalizadorTelefone.VariantesContatoWhatsApp(c.ArgAt<string>(2)).Contains(a.ContatoIdExterno)));
        _conversas.AddAsync(Arg.Any<Conversa>(), Arg.Any<CancellationToken>())
            .Returns(c => { _abertas.Add(c.Arg<Conversa>()); return Task.CompletedTask; });
        _conversas.ObterPorIdAsync(_empresa, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _abertas.FirstOrDefault(a => a.Id == c.ArgAt<Guid>(1)));
        _conversas.ObterPorIdComLockAsync(_empresa, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _abertas.FirstOrDefault(a => a.Id == c.ArgAt<Guid>(1)));
        _conversas.ObterMensagemAsync(_empresa, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _mensagens.FirstOrDefault(m => m.Id == c.ArgAt<Guid>(2) && m.ConversaId == c.ArgAt<Guid>(1)));
        _conversas.ObterMensagemComLockAsync(_empresa, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _mensagens.FirstOrDefault(m => m.Id == c.ArgAt<Guid>(2) && m.ConversaId == c.ArgAt<Guid>(1)));
        _conversas.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>())
            .Returns(c => { _mensagens.Add(c.Arg<Mensagem>()); return Task.CompletedTask; });

        _canal.Canal.Returns(CanalConversa.WhatsApp);
        _canal.EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns("wamid.primeiro");
        _catalogo.ListarAsync(_empresa, Arg.Any<CancellationToken>()).Returns([
            new ModeloAtendimento("boas_vindas", "pt_BR", "UTILITY", "Olá {{1}}, aqui é a Casa da Baba.", 1, null, null, null)]);
    }

    private IniciarConversaClienteUseCase UseCase()
    {
        var envio = new EnviarModeloConversaUseCase(_conversas, _clientes, _catalogo, new(_consentimentos), new([_canal]), _uow, TimeProvider.System);
        return new(_conversas, _clientes, _catalogo, envio, _uow, TimeProvider.System);
    }

    private IniciarConversaClienteCommand Command(Guid? tentativa = null, string modelo = "boas_vindas") =>
        new(_empresa, _usuario, _cliente.Id, tentativa ?? Guid.NewGuid(), modelo, "pt_BR", ["Ana"]);

    [Fact]
    public async Task ClienteSemConversa_AbreConversaVinculadaEEnviaOModelo()
    {
        var r = await UseCase().ExecuteAsync(Command());

        var conversa = _abertas.Should().ContainSingle().Subject;
        r.ConversaId.Should().Be(conversa.Id);
        conversa.ClienteId.Should().Be(_cliente.Id);
        conversa.Canal.Should().Be(CanalConversa.WhatsApp);
        conversa.ContatoIdExterno.Should().Be("5511988887777", "E.164 do cadastro, sem o +");
        conversa.ContatoNome.Should().Be("Ana Souza");
        conversa.UltimaMensagemEntradaEm.Should().BeNull("o cliente nunca escreveu: fora da janela, só modelo");
        conversa.AssumidaPorUsuarioId.Should().Be(_usuario);
        r.Mensagem.Status.Should().Be(StatusMensagem.Enviada);
        r.Mensagem.Texto.Should().Be("Olá Ana, aqui é a Casa da Baba.");
        await _canal.Received(1).EnviarModeloAsync("5511988887777", "boas_vindas", "pt_BR",
            Arg.Is<IReadOnlyList<string>>(p => p.SequenceEqual(new[] { "Ana" })), Arg.Any<CancellationToken>());
        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task MesmaTentativaRepetida_NaoAbreOutraConversaNemReenvia()
    {
        var tentativa = Guid.NewGuid();

        var primeira = await UseCase().ExecuteAsync(Command(tentativa));
        var repetida = await UseCase().ExecuteAsync(Command(tentativa));

        repetida.ConversaId.Should().Be(primeira.ConversaId);
        repetida.Mensagem.Id.Should().Be(primeira.Mensagem.Id);
        _abertas.Should().ContainSingle();
        _mensagens.Should().ContainSingle();
        await _canal.Received(1).EnviarModeloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConversaAbertaDoMesmoContato_EhReaproveitadaEGanhaOVinculo()
    {
        // Lead que escreveu antes do cadastro, com o wa_id sem o nono dígito (número antigo).
        var lead = Conversa.Abrir(_empresa, "551188887777", DateTime.UtcNow.AddDays(-3));
        _abertas.Add(lead);

        var r = await UseCase().ExecuteAsync(Command());

        r.ConversaId.Should().Be(lead.Id);
        _abertas.Should().ContainSingle();
        lead.ClienteId.Should().Be(_cliente.Id);
        await _canal.Received(1).EnviarModeloAsync("551188887777", "boas_vindas", "pt_BR",
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _conversas.DidNotReceive().AddAsync(Arg.Any<Conversa>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConversaAbertaDeOutroCliente_Recusa()
    {
        _abertas.Add(Conversa.Abrir(_empresa, "5511988887777", DateTime.UtcNow.AddDays(-1), clienteId: Guid.NewGuid()));

        await FluentActions.Awaiting(() => UseCase().ExecuteAsync(Command()))
            .Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*outro cliente*");
        await _canal.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task ModeloNaoAprovado_NaoDeixaConversaVazia()
    {
        await FluentActions.Awaiting(() => UseCase().ExecuteAsync(Command(modelo: "inexistente")))
            .Should().ThrowAsync<UseCaseValidationException>();

        _abertas.Should().BeEmpty();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task SemConexaoDoWhatsApp_PropagaOErroDoCatalogoSemAbrirConversa()
    {
        _catalogo.ListarAsync(_empresa, Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<ModeloAtendimento>>>(
            _ => throw CatalogoModelosException.SemConexao("Conecte o WhatsApp em Integrações."));

        (await FluentActions.Awaiting(() => UseCase().ExecuteAsync(Command()))
            .Should().ThrowAsync<CatalogoModelosException>()).Which.FaltaConexao.Should().BeTrue();
        _abertas.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    public async Task ClienteSemTelefoneValido_Recusa(string? telefone)
    {
        _cliente.Telefone = telefone;

        await FluentActions.Awaiting(() => UseCase().ExecuteAsync(Command()))
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage("*telefone*");
        _abertas.Should().BeEmpty();
    }

    [Fact]
    public async Task ClienteBloqueado_Recusa()
    {
        _cliente.Bloquear("inadimplente", DateTime.UtcNow);

        await FluentActions.Awaiting(() => UseCase().ExecuteAsync(Command()))
            .Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*bloqueado*");
        _abertas.Should().BeEmpty();
    }

    [Fact]
    public async Task ClienteDeOutraEmpresa_NaoEncontrado()
    {
        await FluentActions.Awaiting(() => UseCase().ExecuteAsync(Command() with { ClienteId = Guid.NewGuid() }))
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Cliente não encontrado*");
    }
}
