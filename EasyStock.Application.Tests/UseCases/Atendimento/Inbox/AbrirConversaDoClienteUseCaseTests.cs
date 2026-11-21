using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Inbox;

/// <summary>
/// #2871: "Novo pedido" na ficha, em Pedidos e no Atendimento. O cliente falou no WhatsApp pessoal da dona; a equipe
/// registra o pedido pelo Console. Abre (ou reaproveita) a conversa do WhatsApp da loja com o cliente, sem mandar
/// mensagem: a comanda nasce nela e o resumo do pedido é o primeiro contato.
/// </summary>
public class AbrirConversaDoClienteUseCaseTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly List<Conversa> _abertas = [];
    private readonly Cliente _cliente;

    public AbrirConversaDoClienteUseCaseTests()
    {
        _cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = _empresa, Nome = "Ana Souza", Telefone = "(11) 98888-7777", Ativo = true };
        _clientes.GetByIdAsync(_empresa, _cliente.Id).Returns(_ => _cliente);
        _conversas.ObterAbertaPorContatoAsync(_empresa, CanalConversa.WhatsApp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(c => _abertas.FirstOrDefault(a => a.EstaAberta
                && NormalizadorTelefone.VariantesContatoWhatsApp(c.ArgAt<string>(2)).Contains(a.ContatoIdExterno)));
        _conversas.AddAsync(Arg.Any<Conversa>(), Arg.Any<CancellationToken>())
            .Returns(c => { _abertas.Add(c.Arg<Conversa>()); return Task.CompletedTask; });
        _conversas.ObterPorIdComLockAsync(_empresa, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _abertas.FirstOrDefault(a => a.Id == c.ArgAt<Guid>(1)));
    }

    private AbrirConversaDoClienteUseCase UseCase() =>
        new(_clientes, new ConversaWhatsAppDoCliente(_conversas, _uow, TimeProvider.System), _uow, TimeProvider.System);

    private Task<ConversaResumoResult> Abrir(Guid? clienteId = null, string? celular = null) =>
        UseCase().ExecuteAsync(new AbrirConversaDoClienteCommand(_empresa, _usuario, clienteId ?? _cliente.Id, celular));

    [Fact]
    public async Task ClienteSemConversa_AbreConversaVinculadaSemMandarMensagem()
    {
        var r = await Abrir();

        var conversa = _abertas.Should().ContainSingle().Subject;
        r.Id.Should().Be(conversa.Id);
        r.ClienteId.Should().Be(_cliente.Id);
        r.Situacao.Should().NotBe(SituacaoConversa.Encerrada);
        r.UltimaMensagemTexto.Should().BeNull();
        conversa.ClienteId.Should().Be(_cliente.Id);
        conversa.Canal.Should().Be(CanalConversa.WhatsApp);
        conversa.ContatoIdExterno.Should().Be("5511988887777");
        conversa.ContatoNome.Should().Be("Ana Souza");
        _uow.CommitCount.Should().Be(1);
        await _conversas.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
    }

    /// <summary>#2902: a dona abriu a conversa para atender pessoalmente; o Assistente não pode responder nela.</summary>
    [Fact]
    public async Task ConversaCriada_NasceAssumidaPeloUsuarioQueClicou()
    {
        var r = await Abrir();

        var conversa = _abertas.Should().ContainSingle().Subject;
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        conversa.AssumidaPorUsuarioId.Should().Be(_usuario);
        r.Situacao.Should().Be(SituacaoConversa.Assumida);
    }

    /// <summary>#2902: reaproveitar não tira do Assistente uma conversa que já existia.</summary>
    [Fact]
    public async Task ConversaAutomaticaExistente_ContinuaAutomatica()
    {
        var existente = Conversa.Abrir(_empresa, "5511988887777", DateTime.UtcNow.AddDays(-1), clienteId: _cliente.Id);
        _abertas.Add(existente);

        await Abrir();

        existente.Situacao.Should().Be(SituacaoConversa.Automatica);
        existente.AssumidaPorUsuarioId.Should().BeNull();
    }

    [Fact]
    public async Task ConversaAbertaDoMesmoContato_EhReaproveitadaEGanhaOVinculo()
    {
        var lead = Conversa.Abrir(_empresa, "551188887777", DateTime.UtcNow.AddDays(-3));
        _abertas.Add(lead);

        var r = await Abrir();

        r.Id.Should().Be(lead.Id);
        r.ClienteId.Should().Be(_cliente.Id);
        lead.ClienteId.Should().Be(_cliente.Id);
        _abertas.Should().ContainSingle();
    }

    [Fact]
    public async Task ConversaAbertaDeOutroCliente_Recusa()
    {
        _abertas.Add(Conversa.Abrir(_empresa, "5511988887777", DateTime.UtcNow.AddDays(-1), clienteId: Guid.NewGuid()));

        await FluentActions.Awaiting(() => Abrir())
            .Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*outro cliente*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("123")]
    [InlineData("(11) 3333-4444")]
    public async Task ClienteSemCelularValido_RecusaSemAbrir(string? telefone)
    {
        _cliente.Telefone = telefone;

        await FluentActions.Awaiting(() => Abrir())
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage("*celular*");
        _abertas.Should().BeEmpty();
    }

    /// <summary>#2930: 11 de 24 clientes com pedido estavam sem telefone; o Novo pedido pede o celular ali mesmo e grava.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ClienteSemTelefone_ComCelularValido_GravaNoCadastroEAbre(string? telefone)
    {
        _cliente.Telefone = telefone;

        await Abrir(celular: "(11) 98888-7777");

        _cliente.Telefone.Should().Be("11988887777");
        await _clientes.Received(1).UpdateAsync(_cliente);
        _abertas.Should().ContainSingle().Which.ContatoIdExterno.Should().Be("5511988887777");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("(11) 3333-4444")]
    public async Task ClienteSemTelefone_ComCelularInvalido_RecusaSemGravar(string celular)
    {
        _cliente.Telefone = null;

        await FluentActions.Awaiting(() => Abrir(celular: celular))
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage("*celular*");
        _cliente.Telefone.Should().BeNull();
        await _clientes.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        _abertas.Should().BeEmpty();
    }

    [Fact]
    public async Task ClienteComTelefone_IgnoraOCelularInformado()
    {
        await Abrir(celular: "(21) 97777-6666");

        _cliente.Telefone.Should().Be("(11) 98888-7777");
        await _clientes.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        _abertas.Should().ContainSingle().Which.ContatoIdExterno.Should().Be("5511988887777");
    }

    [Fact]
    public async Task ClienteDesativado_Recusa()
    {
        _cliente.Ativo = false;

        await FluentActions.Awaiting(() => Abrir()).Should().ThrowAsync<UseCaseValidationException>().WithMessage("*desativado*");
        _abertas.Should().BeEmpty();
    }

    [Fact]
    public async Task ClienteBloqueado_Recusa()
    {
        _cliente.Bloquear("inadimplente", DateTime.UtcNow);

        await FluentActions.Awaiting(() => Abrir()).Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*bloqueado*");
        _abertas.Should().BeEmpty();
    }

    [Fact]
    public async Task ClienteDeOutraEmpresa_NaoEncontrado()
    {
        await FluentActions.Awaiting(() => Abrir(Guid.NewGuid()))
            .Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Cliente não encontrado*");
    }
}
