using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Automacoes;

/// <summary>S42: automáticas por gatilho respeitam regra ligada, janela e consentimento, e só uma sai na entrada.</summary>
public class AutomacoesHandlerTests
{
    // 30/09/2026 15:00 UTC = 12:00 em São Paulo.
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ClienteEntity _cliente;
    private readonly Conversa _conversa;
    private readonly IRegraAutomaticaRepository _regras = Substitute.For<IRegraAutomaticaRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IExpedienteLojaRepository _expedientes = Substitute.For<IExpedienteLojaRepository>();
    private readonly IConsentimentoContatoRepository _consentimentos = Substitute.For<IConsentimentoContatoRepository>();
    private readonly ICanalMensageria _whats = Substitute.For<ICanalMensageria>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));

    public AutomacoesHandlerTests()
    {
        _cliente = new ClienteEntity { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Ana Souza", Telefone = "11988887777" };
        _clientes.GetByIdAsync(_empresaId, _cliente.Id).Returns(_cliente);
        _consentimentos.ListarDoClienteAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns(new List<ConsentimentoContato>());
        _whats.Canal.Returns(CanalConversa.WhatsApp);
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.1");

        _conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora.AddMinutes(-2));
        _conversa.RegistrarEntrada(Agora.AddMinutes(-1));
        _conversa.VincularCliente(_cliente.Id);
        _conversas.ObterPorIdAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
    }

    private DispararAutomacaoUseCase UseCase() =>
        new(_regras, _conversas, _clientes, _expedientes, new VariaveisAtendimento(_clientes, _pedidos),
            new PoliticaEnvioCliente(_consentimentos), new ResolvedorCanal([_whats]), _uow, _relogio,
            NullLogger<DispararAutomacaoUseCase>.Instance);

    private void Regra(GatilhoAutomacao gatilho, string texto, bool ligada = true) =>
        _regras.ObterPorGatilhoAsync(_empresaId, gatilho, Arg.Any<CancellationToken>())
            .Returns(RegraAutomatica.Criar(_empresaId, gatilho, texto, ligada, Agora));

    private void Expediente(ControleManualLoja controle)
    {
        var expediente = ExpedienteLoja.CriarPadrao(_empresaId);
        expediente.DefinirHorarios([]);
        expediente.DefinirControle(controle, null, Agora);
        _expedientes.GetByEmpresaIdAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(expediente);
    }

    [Fact]
    public async Task ForaDoHorarioVencePrimeiroContato()
    {
        Regra(GatilhoAutomacao.PrimeiroContato, "Oi {nome}!");
        Regra(GatilhoAutomacao.ForaDoHorario, "Estamos fora do horário, {nome}.");
        Regra(GatilhoAutomacao.LojaFechada, "Hoje não abrimos.");
        // Sem turno cadastrado e sem controle manual: fora do horário.
        Expediente(ControleManualLoja.Automatico);

        var resultado = await UseCase().DispararPrimeiraEntradaAsync(_empresaId, _conversa.Id);

        resultado.Should().Be(ResultadoAutomacao.Enviada);
        await _whats.Received(1).EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _whats.Received(1).EnviarTextoAsync(_conversa.ContatoIdExterno, "Estamos fora do horário, Ana.", Arg.Any<CancellationToken>());
        await _conversas.Received(1).AddMensagemAsync(Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Sistema), Arg.Any<CancellationToken>());
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task LojaFechadaNaMaoVenceForaDoHorario()
    {
        Regra(GatilhoAutomacao.ForaDoHorario, "Fora do horário.");
        Regra(GatilhoAutomacao.LojaFechada, "Hoje não abrimos.");
        Expediente(ControleManualLoja.ForcarFechada);

        (await UseCase().DispararPrimeiraEntradaAsync(_empresaId, _conversa.Id)).Should().Be(ResultadoAutomacao.Enviada);

        await _whats.Received(1).EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _whats.Received(1).EnviarTextoAsync(_conversa.ContatoIdExterno, "Hoje não abrimos.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LojaAbertaNaMaoDisparaPrimeiroContato()
    {
        Regra(GatilhoAutomacao.PrimeiroContato, "Oi {nome}!");
        Expediente(ControleManualLoja.ForcarAberta);

        (await UseCase().DispararPrimeiraEntradaAsync(_empresaId, _conversa.Id)).Should().Be(ResultadoAutomacao.Enviada);

        await _whats.Received(1).EnviarTextoAsync(_conversa.ContatoIdExterno, "Oi Ana!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegraDesligada()
    {
        Regra(GatilhoAutomacao.PosEntrega, "Chegou tudo bem?", ligada: false);

        var resultado = await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.PosEntrega, _conversa.Id, null, null));

        resultado.Should().Be(ResultadoAutomacao.Desligada);
        await _whats.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _uow.CommitCount.Should().Be(0);
    }

    [Theory]
    [InlineData(GatilhoAutomacao.Encerramento)]
    [InlineData(GatilhoAutomacao.PagamentoConfirmado)]
    [InlineData(GatilhoAutomacao.PosEntrega)]
    public async Task ClienteBloqueadoNaoRecebeAutomatica(GatilhoAutomacao gatilho)
    {
        // #1292: bloqueio vale em todos os canais e mensagens (S24), não só na primeira entrada.
        Regra(gatilho, "Mensagem automática.");
        _cliente.Bloquear("calote", Agora);

        var resultado = await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, gatilho, _conversa.Id, null, null));

        resultado.Should().Be(ResultadoAutomacao.ClienteBloqueado);
        await _whats.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task SemRegraNaoDispara()
    {
        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.Encerramento, _conversa.Id, null, null)))
            .Should().Be(ResultadoAutomacao.SemRegra);
    }

    [Fact]
    public async Task ForaDaJanelaNaoEnvia()
    {
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Pagamento recebido!");
        var velha = Conversa.Abrir(_empresaId, "5511977776666", Agora.AddDays(-3));
        velha.RegistrarEntrada(Agora.AddDays(-2));
        _conversas.ObterPorIdAsync(_empresaId, velha.Id, Arg.Any<CancellationToken>()).Returns(velha);

        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.PagamentoConfirmado, velha.Id, null, null)))
            .Should().Be(ResultadoAutomacao.ForaDaJanela);
        await _whats.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task VariavelSemValorNaoEnvia()
    {
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Pedido {pedido} pago.");

        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.PagamentoConfirmado, _conversa.Id, null, null)))
            .Should().Be(ResultadoAutomacao.VariavelSemValor);
        await _whats.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task SemConversaPeloIdUsaAMaisRecenteDoCliente()
    {
        Regra(GatilhoAutomacao.PagamentoConfirmado, "Pedido {pedido} pago, {nome}.");
        var pedidoId = Guid.Parse("abcdef12-0000-0000-0000-000000000000");
        _conversas.ListarPorClienteAsync(_empresaId, _cliente.Id, 1, Arg.Any<CancellationToken>()).Returns(new List<Conversa> { _conversa });

        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.PagamentoConfirmado, null, _cliente.Id, pedidoId)))
            .Should().Be(ResultadoAutomacao.Enviada);

        await _whats.Received(1).EnviarTextoAsync(_conversa.ContatoIdExterno, "Pedido ABCDEF12 pago, Ana.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EncerramentoSaiComAConversaJaEncerrada()
    {
        Regra(GatilhoAutomacao.Encerramento, "Obrigada, {nome}!");
        _conversa.Encerrar(Agora);

        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.Encerramento, _conversa.Id, null, null)))
            .Should().Be(ResultadoAutomacao.Enviada);
    }

    [Fact]
    public async Task FalhaDoCanalNaoLanca()
    {
        Regra(GatilhoAutomacao.PosEntrega, "Chegou?");
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("caiu"));

        (await UseCase().ExecuteAsync(new DisparoAutomacao(_empresaId, GatilhoAutomacao.PosEntrega, _conversa.Id, null, null)))
            .Should().Be(ResultadoAutomacao.Falhou);
        _uow.CommitCount.Should().Be(0);
    }
}
