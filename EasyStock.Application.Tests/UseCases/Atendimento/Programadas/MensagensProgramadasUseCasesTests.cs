using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Programadas;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Programadas;

/// <summary>S39: agendar confere destino, janela e consentimento; o disparo confere de novo e envia pelo canal.</summary>
public class MensagensProgramadasUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 9, 28, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ClienteEntity _cliente;
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IMensagemProgramadaRepository _repo = Substitute.For<IMensagemProgramadaRepository>();
    private readonly IConsentimentoContatoRepository _consentimentos = Substitute.For<IConsentimentoContatoRepository>();
    private readonly ICanalMensageria _whats = Substitute.For<ICanalMensageria>();
    private readonly ICanalMensageria _sms = Substitute.For<ICanalMensageria>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));

    public MensagensProgramadasUseCasesTests()
    {
        _cliente = new ClienteEntity { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Fulana", Telefone = "(11) 98888-7777" };
        _clientes.GetByIdAsync(_empresaId, _cliente.Id).Returns(_cliente);
        _consentimentos.ListarDoClienteAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns(new List<ConsentimentoContato>());
        _whats.Canal.Returns(CanalConversa.WhatsApp);
        _sms.Canal.Returns(CanalConversa.Sms);
    }

    private AgendarMensagemProgramadaUseCase Agendar() =>
        new(_clientes, _conversas, _repo, new PoliticaEnvioCliente(_consentimentos), _uow, _relogio);

    private DispararMensagemProgramadaUseCase Disparar() =>
        new(_repo, _clientes, _conversas, new PoliticaEnvioCliente(_consentimentos),
            new ResolvedorCanal([_whats, _sms]), Substitute.For<IOperacaoEventPublisher>(), _uow, _relogio,
            NullLogger<DispararMensagemProgramadaUseCase>.Instance);

    private Conversa ConversaAberta(DateTime ultimaEntrada)
    {
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", ultimaEntrada.AddMinutes(-1));
        conversa.RegistrarEntrada(ultimaEntrada);
        conversa.VincularCliente(_cliente.Id);
        _conversas.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(conversa);
        return conversa;
    }

    private AgendarMensagemProgramadaCommand Comando(CanalConversa canal, DateTime para, string? texto = "Lembrete",
        ModeloMensagem? modelo = null, FinalidadeContato finalidade = FinalidadeContato.Transacional) =>
        new(_empresaId, Guid.NewGuid(), _cliente.Id, null, canal, finalidade, texto, modelo, para);

    [Fact]
    public async Task Agendar_ClienteBloqueado_RecusaSemGravar()
    {
        _cliente.Bloquear("Teste", Agora);

        var act = () => Agendar().ExecuteAsync(Comando(CanalConversa.Sms, Agora.AddHours(1)));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*bloqueado*");
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Disparar_ClienteBloqueadoAposAgendar_RecusaTextoEModelo()
    {
        foreach (var modelo in new ModeloMensagem?[] { null, new("aviso", "pt_BR", []) })
        {
            var mensagem = MensagemProgramada.Agendar(_empresaId, _cliente.Id, null, CanalConversa.WhatsApp,
                FinalidadeContato.Transacional, modelo is null ? "Oi" : null, modelo,
                Agora.AddMinutes(1), Guid.NewGuid(), Agora.AddMinutes(-10));
            mensagem.Reservar(Agora);
            _repo.ObterAsync(_empresaId, mensagem.Id, Arg.Any<CancellationToken>()).Returns(mensagem);
            _cliente.Bloquear("Teste", Agora);

            await Disparar().ExecuteAsync(_empresaId, mensagem.Id);

            mensagem.Situacao.Should().Be(SituacaoMensagemProgramada.Falhou);
            mensagem.Erro.Should().Contain("bloqueado");
        }
        await _whats.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        await _whats.DidNotReceiveWithAnyArgs().EnviarModeloAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Agendar_WhatsAppTextoComJanelaVencidaNoEnvio_Recusa()
    {
        ConversaAberta(Agora.AddHours(-2));

        var act = () => Agendar().ExecuteAsync(Comando(CanalConversa.WhatsApp, Agora.AddHours(23)));

        (await act.Should().ThrowAsync<UseCaseValidationException>()).WithMessage("*modelo aprovado*");
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Agendar_SmsDeMarketingSemConsentimento_Recusa()
    {
        var act = () => Agendar().ExecuteAsync(Comando(CanalConversa.Sms, Agora.AddHours(1), finalidade: FinalidadeContato.Marketing));

        (await act.Should().ThrowAsync<UseCaseValidationException>()).WithMessage("*não autorizou*");
    }

    [Fact]
    public async Task Agendar_NoPassado_Recusa()
    {
        var act = () => Agendar().ExecuteAsync(Comando(CanalConversa.Sms, Agora.AddMinutes(-5)));

        (await act.Should().ThrowAsync<UseCaseValidationException>()).WithMessage("*passado*");
    }

    [Fact]
    public async Task Agendar_SmsTransacionalPeloTelefoneDoCadastro_Grava()
    {
        var resultado = await Agendar().ExecuteAsync(Comando(CanalConversa.Sms, Agora.AddHours(1)));

        resultado.Situacao.Should().Be(SituacaoMensagemProgramada.Agendada);
        await _repo.Received(1).AddAsync(Arg.Is<MensagemProgramada>(m => m.Canal == CanalConversa.Sms), Arg.Any<CancellationToken>());
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Agendar_TelefoneDoCadastroComDdiSemMais_ProcuraAConversaAberta()
    {
        // #1290: o VO Telefone grava "55..." sem '+'; antes o destino recusava e o agendamento dizia
        // que o cliente não tinha telefone.
        _cliente.Telefone = "5511988887777";
        ConversaAberta(Agora.AddHours(-1));

        var resultado = await Agendar().ExecuteAsync(Comando(CanalConversa.WhatsApp, Agora.AddHours(1)));

        resultado.ConversaId.Should().NotBeNull();
        await _conversas.Received(1).ObterAbertaPorContatoAsync(
            _empresaId, CanalConversa.WhatsApp, "+5511988887777", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disparar_ConversaDoAgendamentoEncerrada_UsaAConversaAbertaAtualDoContato()
    {
        // #1290: a conversa do agendamento foi encerrada e o cliente voltou a falar numa nova.
        var antiga = Conversa.Abrir(_empresaId, "5511988887777", Agora.AddDays(-3));
        antiga.VincularCliente(_cliente.Id);
        antiga.Encerrar(Agora.AddDays(-2));
        var atual = ConversaAberta(Agora.AddHours(-1));
        var mensagem = MensagemProgramada.Agendar(_empresaId, _cliente.Id, antiga.Id, CanalConversa.WhatsApp,
            FinalidadeContato.Transacional, "Chegou o bolo", null, Agora.AddMinutes(1), Guid.NewGuid(), Agora.AddDays(-3));
        mensagem.Reservar(Agora);
        _repo.ObterAsync(_empresaId, mensagem.Id, Arg.Any<CancellationToken>()).Returns(mensagem);
        _conversas.ObterPorIdAsync(_empresaId, antiga.Id, Arg.Any<CancellationToken>()).Returns(antiga);
        _whats.EnviarTextoAsync("5511988887777", "Chegou o bolo", Arg.Any<CancellationToken>()).Returns("wamid.atual");

        await Disparar().ExecuteAsync(_empresaId, mensagem.Id);

        mensagem.Situacao.Should().Be(SituacaoMensagemProgramada.Enviada);
        await _conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.ConversaId == atual.Id && m.ExternoId == "wamid.atual"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disparar_ConversaEncerradaEAAbertaEDeOutroCliente_NaoUsaAConversaAlheia()
    {
        var antiga = Conversa.Abrir(_empresaId, "5511988887777", Agora.AddDays(-3));
        antiga.VincularCliente(_cliente.Id);
        antiga.Encerrar(Agora.AddDays(-2));
        var alheia = ConversaAberta(Agora.AddHours(-1));
        alheia.VincularCliente(Guid.NewGuid());
        var mensagem = MensagemProgramada.Agendar(_empresaId, _cliente.Id, antiga.Id, CanalConversa.WhatsApp,
            FinalidadeContato.Transacional, "Chegou o bolo", null, Agora.AddMinutes(1), Guid.NewGuid(), Agora.AddDays(-3));
        mensagem.Reservar(Agora);
        _repo.ObterAsync(_empresaId, mensagem.Id, Arg.Any<CancellationToken>()).Returns(mensagem);
        _conversas.ObterPorIdAsync(_empresaId, antiga.Id, Arg.Any<CancellationToken>()).Returns(antiga);

        await Disparar().ExecuteAsync(_empresaId, mensagem.Id);

        await _conversas.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
    }

    [Fact]
    public async Task Disparar_TextoDentroDaJanela_SaiPeloCanalEEntraNoHistoricoComSelo()
    {
        var conversa = ConversaAberta(Agora.AddHours(-1));
        var mensagem = MensagemProgramada.Agendar(_empresaId, _cliente.Id, conversa.Id, CanalConversa.WhatsApp,
            FinalidadeContato.Transacional, "Seu pedido sai às 11h", null, Agora.AddMinutes(1), Guid.NewGuid(), Agora.AddMinutes(-10));
        mensagem.Reservar(Agora);
        _repo.ObterAsync(_empresaId, mensagem.Id, Arg.Any<CancellationToken>()).Returns(mensagem);
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        _whats.EnviarTextoAsync("5511988887777", "Seu pedido sai às 11h", Arg.Any<CancellationToken>()).Returns("wamid.prog");

        await Disparar().ExecuteAsync(_empresaId, mensagem.Id);

        mensagem.Situacao.Should().Be(SituacaoMensagemProgramada.Enviada);
        mensagem.IdExterno.Should().Be("wamid.prog");
        await _conversas.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Programada && m.ExternoId == "wamid.prog" && m.Autor == AutorMensagem.Dona),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disparar_JanelaVenceuDesdeOAgendamento_MarcaFalhouComMotivoESemEnviar()
    {
        // Agendada com a janela aberta; na hora do disparo a última entrada já tem mais de 24 h.
        var conversa = ConversaAberta(Agora.AddHours(-25));
        var mensagem = MensagemProgramada.Agendar(_empresaId, _cliente.Id, conversa.Id, CanalConversa.WhatsApp,
            FinalidadeContato.Transacional, "Lembrete", null, Agora.AddMinutes(1), Guid.NewGuid(), Agora.AddHours(-3));
        mensagem.Reservar(Agora);
        _repo.ObterAsync(_empresaId, mensagem.Id, Arg.Any<CancellationToken>()).Returns(mensagem);
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);

        await Disparar().ExecuteAsync(_empresaId, mensagem.Id);

        mensagem.Situacao.Should().Be(SituacaoMensagemProgramada.Falhou);
        mensagem.Erro.Should().Contain("janela");
        await _whats.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Disparar_MensagemQueNaoEstaEnviando_NaoFazNada()
    {
        var cancelada = MensagemProgramada.Agendar(_empresaId, _cliente.Id, null, CanalConversa.Sms,
            FinalidadeContato.Transacional, "Oi", null, Agora.AddMinutes(1), Guid.NewGuid(), Agora.AddMinutes(-10));
        cancelada.Cancelar(Agora);
        _repo.ObterAsync(_empresaId, cancelada.Id, Arg.Any<CancellationToken>()).Returns(cancelada);

        await Disparar().ExecuteAsync(_empresaId, cancelada.Id);

        await _sms.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _uow.CommitCount.Should().Be(0);
    }
}
