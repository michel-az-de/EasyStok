using EasyStock.Application.Tests.Services.Atendimento;
using EasyStock.Application.Events.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Webhook;

public class ProcessarEventoWhatsAppUseCaseTests
{
    private const string PhoneNumberId = "PHONE123";
    private const string ContatoWaId = "5511999998888";

    private readonly IEmpresaRepository _empresaRepository = Substitute.For<IEmpresaRepository>();
    private readonly ITenantFeatureFlagRepository _featureFlagRepository = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly IConfiguracaoAtendimentoRepository _configuracaoRepository = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly IConversaRepository _conversaRepository = Substitute.For<IConversaRepository>();
    private readonly IWebhookRecebidoRepository _webhookRecebidoRepository = Substitute.For<IWebhookRecebidoRepository>();
    private readonly IWhatsAppCloudClient _cloudClient = Substitute.For<IWhatsAppCloudClient>();
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();
    private readonly IOperacaoEventPublisher _eventPublisher = Substitute.For<IOperacaoEventPublisher>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClienteRepository _clienteRepository = Substitute.For<IClienteRepository>();
    private readonly IClienteStorefrontRepository _clienteStorefrontRepository = Substitute.For<IClienteStorefrontRepository>();
    private readonly IStorefrontRepository _storefrontRepository = Substitute.For<IStorefrontRepository>();
    private readonly IConsentimentoContatoRepository _consentimentoRepository = Substitute.For<IConsentimentoContatoRepository>();
    private readonly ICanalMensageria _canalWhatsApp = Substitute.For<ICanalMensageria>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IPublicadorEventoIntegracao _publicadorEventos = Substitute.For<IPublicadorEventoIntegracao>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ProcessarEventoWhatsAppUseCase _useCase;

    public ProcessarEventoWhatsAppUseCaseTests()
    {
        // Antes de construir: o ResolvedorCanal indexa os adaptadores pelo Canal no construtor.
        _canalWhatsApp.Canal.Returns(CanalConversa.WhatsApp);

        _useCase = new ProcessarEventoWhatsAppUseCase(
            _empresaRepository, _featureFlagRepository, _configuracaoRepository, _conversaRepository,
            _webhookRecebidoRepository, _cloudClient, _queueService, _eventPublisher, _tenantContext,
            _unitOfWork,
            new IdentificarClientePorTelefoneUseCase(
                _clienteRepository, _clienteStorefrontRepository,
                NullLogger<IdentificarClientePorTelefoneUseCase>.Instance),
            new SaudacaoAtendimento(_storefrontRepository, new ConfigurationBuilder().Build()),
            new RoteadorAcoesBotao(
                [new ConfirmarEnderecoAcaoBotao(new EscalarConversaUseCase(_conversaRepository, Substitute.For<INotificadorService>(), Substitute.For<IOperacaoEventPublisher>()), ConfirmacaoEnderecoFake.Nova(), _conversaRepository)],
                NullLogger<RoteadorAcoesBotao>.Instance),
            new OptOutPorPalavra(_consentimentoRepository, _conversaRepository, new ResolvedorCanal([_canalWhatsApp]),
                _unitOfWork, NullLogger<OptOutPorPalavra>.Instance),
            new EscalarConversaUseCase(_conversaRepository, _notificador, _eventPublisher),
            NullLogger<ProcessarEventoWhatsAppUseCase>.Instance,
            _publicadorEventos);

        _cloudClient.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnvioWhatsAppResult("wamid.saida"));

        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");
        empresa.Id = _empresaId;
        _empresaRepository.GetByWhatsAppPhoneNumberIdAsync(PhoneNumberId, Arg.Any<CancellationToken>()).Returns(empresa);
        _featureFlagRepository.ListarAtivasAsync(_empresaId, Arg.Any<CancellationToken>())
            .Returns(new[] { FeatureCatalogo.ModuloAtendimento });
        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns((ConfiguracaoAtendimento?)null);
        _conversaRepository.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, ContatoWaId, Arg.Any<CancellationToken>())
            .Returns((Conversa?)null);

        // Idempotência: primeira vez sempre registra com sucesso.
        _webhookRecebidoRepository
            .TryRegistrarAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => WebhookRecebido.Criar("meta_whatsapp", callInfo.ArgAt<string>(1), callInfo.ArgAt<string>(2)));
    }

    private static string PayloadTexto(string wamid, string texto) => """
        {"entry":[{"changes":[{"value":{
            "metadata":{"phone_number_id":"__PHONE__"},
            "contacts":[{"profile":{"name":"Fulano"},"wa_id":"__WAID__"}],
            "messages":[{"from":"__WAID__","id":"__WAMID__","timestamp":"1700000000","type":"text","text":{"body":"__TEXTO__"}}]
        }}]}]}
        """
        .Replace("__PHONE__", PhoneNumberId).Replace("__WAID__", ContatoWaId)
        .Replace("__WAMID__", wamid).Replace("__TEXTO__", texto);

    private static string PayloadImagem(string wamid, string mediaId) => """
        {"entry":[{"changes":[{"value":{
            "metadata":{"phone_number_id":"__PHONE__"},
            "contacts":[{"profile":{"name":"Fulano"},"wa_id":"__WAID__"}],
            "messages":[{"from":"__WAID__","id":"__WAMID__","timestamp":"1700000000","type":"image","image":{"id":"__MEDIA__","mime_type":"image/jpeg"}}]
        }}]}]}
        """
        .Replace("__PHONE__", PhoneNumberId).Replace("__WAID__", ContatoWaId)
        .Replace("__WAMID__", wamid).Replace("__MEDIA__", mediaId);

    private static string PayloadBotaoAcao(string wamid, string botaoId) => """
        {"entry":[{"changes":[{"value":{
            "metadata":{"phone_number_id":"__PHONE__"},
            "contacts":[{"profile":{"name":"Fulano"},"wa_id":"__WAID__"}],
            "messages":[{"from":"__WAID__","id":"__WAMID__","timestamp":"1700000000","type":"interactive","interactive":{"type":"button_reply","button_reply":{"id":"__BOTAO__","title":"Confirmar"}}}]
        }}]}]}
        """
        .Replace("__PHONE__", PhoneNumberId).Replace("__WAID__", ContatoWaId)
        .Replace("__WAMID__", wamid).Replace("__BOTAO__", botaoId);

    private static string PayloadStatus(string wamid, string status) => """
        {"entry":[{"changes":[{"value":{
            "metadata":{"phone_number_id":"__PHONE__"},
            "statuses":[{"id":"__WAMID__","status":"__STATUS__","recipient_id":"__WAID__"}]
        }}]}]}
        """
        .Replace("__PHONE__", PhoneNumberId).Replace("__WAID__", ContatoWaId)
        .Replace("__WAMID__", wamid).Replace("__STATUS__", status);

    [Fact]
    public async Task TextoEnfileiraAgente()
    {
        await _useCase.ExecuteAsync(PayloadTexto("wamid.texto1", "Oi, tudo bem?"));

        await _conversaRepository.Received(1).AddAsync(Arg.Any<Conversa>(), Arg.Any<CancellationToken>());
        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.TipoConteudo == TipoConteudoMensagem.Texto && m.Texto == "Oi, tudo bem?"),
            Arg.Any<CancellationToken>());
        await _queueService.Received(1).EnqueueAsync(
            FilaAtendimentoNomes.TurnoAgente, Arg.Any<ProcessarTurnoAgenteJob>());
        await _webhookRecebidoRepository.Received(1).MarcarProcessadoAsync(
            Arg.Any<Guid>(), sucesso: true, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BotaoAcaoNaoChamaAgente()
    {
        await _useCase.ExecuteAsync(PayloadBotaoAcao("wamid.botao1", "acao:confirmar_endereco:123"));

        await _queueService.DidNotReceiveWithAnyArgs().EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, default(ProcessarTurnoAgenteJob)!);
        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.BotaoId == "acao:confirmar_endereco:123"), Arg.Any<CancellationToken>());
        // S06: o roteador resolveu a ação (stub que escala até a S14) sem enfileirar o agente.
        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Sistema && m.Texto!.Contains("confirmou o endereço 123")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImagemArmazena()
    {
        await _useCase.ExecuteAsync(PayloadImagem("wamid.imagem1", "media-1"));

        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.TipoConteudo == TipoConteudoMensagem.Imagem), Arg.Any<CancellationToken>());
        await _queueService.Received(1).EnqueueAsync(
            FilaAtendimentoNomes.MidiaWhatsApp,
            Arg.Is<ArmazenarMidiaWhatsAppJob>(j => j.Wamid == "wamid.imagem1" && j.MediaId == "media-1"));
    }

    [Fact]
    public async Task WamidDuplicadoNaoReprocessa()
    {
        var wamid = "wamid.dup1";
        var jaProcessado = WebhookRecebido.Criar("meta_whatsapp", wamid, "hash");
        jaProcessado.MarcarProcessado(sucesso: true);

        _webhookRecebidoRepository
            .TryRegistrarAsync("meta_whatsapp", wamid, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((WebhookRecebido?)null);
        _webhookRecebidoRepository.ObterAsync("meta_whatsapp", wamid, Arg.Any<CancellationToken>())
            .Returns(jaProcessado);

        await _useCase.ExecuteAsync(PayloadTexto(wamid, "Oi de novo"));

        await _conversaRepository.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
        await _queueService.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default(ProcessarTurnoAgenteJob)!);
    }

    [Fact]
    public async Task StatusAtualizaMensagem()
    {
        var wamid = "wamid.status1";
        var mensagem = Mensagem.Saida(_empresaId, Guid.NewGuid(), AutorMensagem.Agente, DateTime.UtcNow, TipoConteudoMensagem.Texto, "Oi", wamid);
        _conversaRepository.ObterMensagemPorExternoIdAsync(_empresaId, wamid, Arg.Any<CancellationToken>()).Returns(mensagem);

        await _useCase.ExecuteAsync(PayloadStatus(wamid, "read"));

        mensagem.Status.Should().Be(StatusMensagem.Lida);
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task StatusFalhouGravaErro()
    {
        var wamid = "wamid.status2";
        var mensagem = Mensagem.Saida(_empresaId, Guid.NewGuid(), AutorMensagem.Agente, DateTime.UtcNow, TipoConteudoMensagem.Texto, "Oi", wamid);
        _conversaRepository.ObterMensagemPorExternoIdAsync(_empresaId, wamid, Arg.Any<CancellationToken>()).Returns(mensagem);

        var payload = """
            {"entry":[{"changes":[{"value":{
                "metadata":{"phone_number_id":"__PHONE__"},
                "statuses":[{"id":"__WAMID__","status":"failed","recipient_id":"__WAID__","errors":[{"title":"Erro generico"}]}]
            }}]}]}
            """
            .Replace("__PHONE__", PhoneNumberId).Replace("__WAID__", ContatoWaId).Replace("__WAMID__", wamid);

        await _useCase.ExecuteAsync(payload);

        mensagem.Status.Should().Be(StatusMensagem.Falhou);
        mensagem.Erro.Should().Be("Erro generico");
        mensagem.ProximoReenvioEm.Should().BeNull("sem código da Meta não há como saber se é temporária");
    }

    [Theory]
    [InlineData(131000, true)]
    [InlineData(130429, true)]
    [InlineData(131016, true)]
    [InlineData(133004, true)]
    [InlineData(131030, false)]
    [InlineData(131026, false)]
    [InlineData(131047, false)]
    [InlineData(131051, false)]
    public async Task StatusFalhouAgendaReenvioSoQuandoTemporario(int codigo, bool agenda)
    {
        var wamid = "wamid.falha" + codigo;
        var mensagem = Mensagem.Saida(_empresaId, Guid.NewGuid(), AutorMensagem.Dona, DateTime.UtcNow, TipoConteudoMensagem.Texto, "Oi", wamid);
        _conversaRepository.ObterMensagemPorExternoIdAsync(_empresaId, wamid, Arg.Any<CancellationToken>()).Returns(mensagem);

        var payload = """
            {"entry":[{"changes":[{"value":{
                "metadata":{"phone_number_id":"__PHONE__"},
                "statuses":[{"id":"__WAMID__","status":"failed","recipient_id":"__WAID__","errors":[{"code":__COD__,"title":"Falha da Meta"}]}]
            }}]}]}
            """
            .Replace("__PHONE__", PhoneNumberId).Replace("__WAID__", ContatoWaId).Replace("__WAMID__", wamid)
            .Replace("__COD__", codigo.ToString());

        await _useCase.ExecuteAsync(payload);

        mensagem.Status.Should().Be(StatusMensagem.Falhou);
        mensagem.Erro.Should().Be("Falha da Meta");
        (mensagem.ProximoReenvioEm is not null).Should().Be(agenda);
    }

    [Fact]
    public async Task PhoneNumberIdDesconhecidoRegistraFalha()
    {
        _empresaRepository.GetByWhatsAppPhoneNumberIdAsync("PHONE-DESCONHECIDO", Arg.Any<CancellationToken>())
            .Returns((Empresa?)null);

        var payload = """
            {"entry":[{"changes":[{"value":{
                "metadata":{"phone_number_id":"PHONE-DESCONHECIDO"},
                "messages":[{"from":"5511999998888","id":"wamid.x","timestamp":"1700000000","type":"text","text":{"body":"oi"}}]
            }}]}]}
            """;

        await _useCase.ExecuteAsync(payload);

        await _webhookRecebidoRepository.Received(1).MarcarProcessadoAsync(
            Arg.Any<Guid>(), sucesso: false, "empresa_desconhecida", Arg.Any<CancellationToken>());
        await _conversaRepository.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
    }

    [Fact]
    public async Task PrimeiraMensagemSaudaAntesDoAgente()
    {
        var existente = ClienteEntity.Criar(_empresaId, "Maria Silva");
        _clienteStorefrontRepository
            .GetByTelefoneHashAsync(_empresaId, ClienteOtp.CalcularTelefoneHash("+" + ContatoWaId), Arg.Any<CancellationToken>())
            .Returns(existente);

        var ordem = new List<string>();
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan? saudacaoEm = null;
        string? textoSaudacao = null;
        _cloudClient.EnviarTextoAsync(ContatoWaId, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                saudacaoEm ??= relogio.Elapsed;
                textoSaudacao = ci.ArgAt<string>(1);
                ordem.Add("saudacao");
                return new EnvioWhatsAppResult("wamid.saudacao");
            });
        await _queueService.EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, Arg.Do<ProcessarTurnoAgenteJob>(_ => ordem.Add("agente")));
        Conversa? conversaAberta = null;
        await _conversaRepository.AddAsync(Arg.Do<Conversa>(c => conversaAberta = c), Arg.Any<CancellationToken>());

        await _useCase.ExecuteAsync(PayloadTexto("wamid.primeira", "Oi"));

        ordem.Should().Equal("saudacao", "agente");
        saudacaoEm.Should().NotBeNull().And.BeLessThan(TimeSpan.FromSeconds(5));
        textoSaudacao.Should().Contain("Maria").And.Contain("/cardapio");
        conversaAberta!.ClienteId.Should().Be(existente.Id);
        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Direcao == DirecaoMensagem.Saida && m.Autor == AutorMensagem.Sistema
                                  && m.ExternoId == "wamid.saudacao" && m.Texto == textoSaudacao),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrimeiraMensagemDeClienteBloqueadoEscalaSemSaudacaoNemAgente()
    {
        var bloqueado = ClienteEntity.Criar(_empresaId, "Maria Silva");
        bloqueado.Bloquear("golpe do Pix", DateTime.UtcNow.AddDays(-1));
        _clienteStorefrontRepository
            .GetByTelefoneHashAsync(_empresaId, ClienteOtp.CalcularTelefoneHash("+" + ContatoWaId), Arg.Any<CancellationToken>())
            .Returns(bloqueado);
        Conversa? conversaAberta = null;
        await _conversaRepository.AddAsync(Arg.Do<Conversa>(c => conversaAberta = c), Arg.Any<CancellationToken>());

        await _useCase.ExecuteAsync(PayloadTexto("wamid.bloqueado", "Oi"));

        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        await _queueService.DidNotReceive().EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, Arg.Any<ProcessarTurnoAgenteJob>());
        conversaAberta!.Situacao.Should().Be(SituacaoConversa.Assumida);
        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Sistema && m.ExternoId == null
                                  && m.Texto!.Contains("cliente bloqueado: golpe do Pix")),
            Arg.Any<CancellationToken>());
        await _notificador.Received(1).EnfileirarEventoAsync(
            EasyStock.Domain.Enums.Notifications.TipoEventoNotificacao.ConversaEscalada, _empresaId,
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task PrimeiraMensagemDeDesconhecidoSaudaPrimeiroContatoECriaLead()
    {
        string? textoSaudacao = null;
        _cloudClient.EnviarTextoAsync(ContatoWaId, Arg.Do<string>(t => textoSaudacao = t), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnvioWhatsAppResult("wamid.saudacao"));

        await _useCase.ExecuteAsync(PayloadTexto("wamid.lead", "Oi"));

        await _clienteRepository.Received(1).AddAsync(Arg.Is<ClienteEntity>(c => c.Nome == "Fulano" && c.OrderCount == 0));
        textoSaudacao.Should().StartWith(ConfiguracaoAtendimento.CriarPadrao(_empresaId).SaudacaoPrimeiroContato.Split('{')[0]);
    }

    [Fact]
    public async Task ConversaAbertaNaoRepeteSaudacao()
    {
        var aberta = Conversa.Abrir(_empresaId, ContatoWaId, DateTime.UtcNow.AddMinutes(-5), "Fulano");
        _conversaRepository.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, ContatoWaId, Arg.Any<CancellationToken>())
            .Returns(aberta);

        await _useCase.ExecuteAsync(PayloadTexto("wamid.segunda", "Quero um bolo"));

        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        await _clienteRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _queueService.Received(1).EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, Arg.Any<ProcessarTurnoAgenteJob>());
    }

    [Fact]
    public async Task FalhaNaSaudacaoNaoImpedeAgente()
    {
        _cloudClient.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<EnvioWhatsAppResult>(_ => throw new HttpRequestException("meta fora"));

        await _useCase.ExecuteAsync(PayloadTexto("wamid.falha", "Oi"));

        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Sistema && m.Status == StatusMensagem.Falhou),
            Arg.Any<CancellationToken>());
        await _queueService.Received(1).EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, Arg.Any<ProcessarTurnoAgenteJob>());
        await _webhookRecebidoRepository.DidNotReceive().MarcarProcessadoAsync(
            Arg.Any<Guid>(), sucesso: false, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsaInstanteDaMetaNaMensagemENaJanela()
    {
        // A Meta reentrega com atraso (API fora do ar): a janela de 24 h conta do envio do cliente,
        // não do processamento — senão o console manda texto livre que a Meta recusa (131047).
        var enviadaPeloCliente = DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime;
        Conversa? aberta = null;
        await _conversaRepository.AddAsync(Arg.Do<Conversa>(c => aberta = c), Arg.Any<CancellationToken>());

        await _useCase.ExecuteAsync(PayloadTexto("wamid.atrasada", "Oi"));

        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.EnviadaEm == enviadaPeloCliente), Arg.Any<CancellationToken>());
        aberta!.UltimaMensagemEntradaEm.Should().Be(enviadaPeloCliente);
    }

    [Fact]
    public async Task FalhaDePersistenciaDescartaAlteracoesEPedeReenvio()
    {
        // Ex.: dois POSTs concorrentes da 1ª mensagem de um contato novo — o segundo viola o índice
        // único da conversa aberta. Sem reenvio da Meta a mensagem se perderia.
        _unitOfWork.CommitAsync().Returns<int>(_ => throw new InvalidOperationException("23505 unique_violation"));

        var completo = await _useCase.ExecuteAsync(PayloadTexto("wamid.corrida", "quero pedir"));

        completo.Should().BeFalse();
        _unitOfWork.Received(1).DescartarAlteracoesPendentes();
        await _webhookRecebidoRepository.Received(1).MarcarProcessadoAsync(
            Arg.Any<Guid>(), sucesso: false, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalhaNaPrimeiraMensagemNaoImpedeAsDemaisDoPayload()
    {
        var chamadas = 0;
        _unitOfWork.CommitAsync().Returns<int>(_ => ++chamadas == 1 ? throw new InvalidOperationException("falha") : 1);
        var payload = """
            {"entry":[{"changes":[{"value":{
                "metadata":{"phone_number_id":"__PHONE__"},
                "contacts":[{"profile":{"name":"Fulano"},"wa_id":"__WAID__"}],
                "messages":[
                  {"from":"__WAID__","id":"wamid.a","timestamp":"1700000000","type":"text","text":{"body":"um"}},
                  {"from":"__WAID__","id":"wamid.b","timestamp":"1700000001","type":"text","text":{"body":"dois"}}]
            }}]}]}
            """.Replace("__PHONE__", PhoneNumberId).Replace("__WAID__", ContatoWaId);

        var completo = await _useCase.ExecuteAsync(payload);

        completo.Should().BeFalse();
        await _webhookRecebidoRepository.Received(1).MarcarProcessadoAsync(
            Arg.Any<Guid>(), sucesso: true, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MensagemJaGravadaNaoDuplicaEMarcaSucesso()
    {
        // Reentrega de um wamid cuja mensagem já foi gravada (1ª tentativa em voo ou marcada como
        // falha depois do commit): não insere de novo — senão viola o índice único para sempre.
        var wamid = "wamid.jagravada";
        var registroEmVoo = WebhookRecebido.Criar("meta_whatsapp", wamid, "hash");
        _webhookRecebidoRepository
            .TryRegistrarAsync("meta_whatsapp", wamid, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((WebhookRecebido?)null);
        _webhookRecebidoRepository.ObterAsync("meta_whatsapp", wamid, Arg.Any<CancellationToken>())
            .Returns(registroEmVoo);
        _conversaRepository.ObterMensagemPorExternoIdAsync(_empresaId, wamid, Arg.Any<CancellationToken>())
            .Returns(Mensagem.Entrada(_empresaId, Guid.NewGuid(), DateTime.UtcNow, TipoConteudoMensagem.Texto, "Oi", wamid));

        var completo = await _useCase.ExecuteAsync(PayloadTexto(wamid, "Oi"));

        completo.Should().BeTrue();
        await _conversaRepository.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
        await _queueService.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default(ProcessarTurnoAgenteJob)!);
        await _webhookRecebidoRepository.Received(1).MarcarProcessadoAsync(
            registroEmVoo.Id, sucesso: true, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegraDeDominioVioladaNaoPedeReenvio()
    {
        // wa_id inválido nunca vai passar: pedir reenvio travaria o payload por 7 dias de retry.
        var payload = """
            {"entry":[{"changes":[{"value":{
                "metadata":{"phone_number_id":"__PHONE__"},
                "messages":[{"from":"123","id":"wamid.curto","timestamp":"1700000000","type":"text","text":{"body":"oi"}}]
            }}]}]}
            """.Replace("__PHONE__", PhoneNumberId);

        var completo = await _useCase.ExecuteAsync(payload);

        completo.Should().BeTrue();
        await _webhookRecebidoRepository.Received(1).MarcarProcessadoAsync(
            Arg.Any<Guid>(), sucesso: false, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SairDeClienteIdentificadoRevogaMarketingENaoAcionaAgente()
    {
        // S38: "sair" revoga o marketing do WhatsApp, confirma pelo canal e o agente não responde.
        var clienteId = Guid.NewGuid();
        var aberta = Conversa.Abrir(_empresaId, ContatoWaId, DateTime.UtcNow.AddMinutes(-5), "Fulano");
        aberta.VincularCliente(clienteId);
        _conversaRepository.ObterAbertaPorContatoAsync(_empresaId, CanalConversa.WhatsApp, ContatoWaId, Arg.Any<CancellationToken>())
            .Returns(aberta);
        _consentimentoRepository.ListarDoClienteAsync(_empresaId, clienteId, Arg.Any<CancellationToken>())
            .Returns(new List<ConsentimentoContato>());

        await _useCase.ExecuteAsync(PayloadTexto("wamid.sair", "Sair"));

        await _consentimentoRepository.Received(1).AddAsync(
            Arg.Is<ConsentimentoContato>(c => c.Canal == CanalConversa.WhatsApp
                && c.Finalidade == FinalidadeContato.Marketing && c.Situacao == SituacaoConsentimento.Revogado),
            Arg.Any<CancellationToken>());
        await _canalWhatsApp.Received(1).EnviarTextoAsync(ContatoWaId, OptOutPorPalavra.Confirmacao, Arg.Any<CancellationToken>());
        await _queueService.DidNotReceiveWithAnyArgs().EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, default(ProcessarTurnoAgenteJob)!);
    }

    private static string PayloadTipo(string wamid, string tipo, string conteudoJson) => """
        {"entry":[{"changes":[{"value":{
            "metadata":{"phone_number_id":"__PHONE__"},
            "contacts":[{"profile":{"name":"Fulano"},"wa_id":"__WAID__"}],
            "messages":[{"from":"__WAID__","id":"__WAMID__","timestamp":"1700000000","type":"__TIPO__",__CONTEUDO__}]
        }}]}]}
        """
        .Replace("__PHONE__", PhoneNumberId).Replace("__WAID__", ContatoWaId)
        .Replace("__WAMID__", wamid).Replace("__TIPO__", tipo).Replace("__CONTEUDO__", conteudoJson);

    [Theory]
    [InlineData("reaction", "\"reaction\":{\"message_id\":\"wamid.saida\",\"emoji\":\"\ud83d\udc4d\"}")]
    [InlineData("unsupported", "\"errors\":[{\"code\":131051,\"title\":\"Message type unknown\"}]")]
    [InlineData("system", "\"system\":{\"body\":\"Cliente trocou de número\",\"type\":\"user_changed_number\"}")]
    public async Task ReacaoSemSuporteOuSistemaGravaSemAcionarAgente(string tipo, string conteudoJson)
    {
        // Issue 1285: o LLM respondia a um emoji. Fica gravado para a dona ver, sem turno do agente
        // e sem saudação ("já te respondo") que nada viria cumprir.
        await _useCase.ExecuteAsync(PayloadTipo($"wamid.{tipo}", tipo, conteudoJson));

        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Direcao == DirecaoMensagem.Entrada && m.TipoConteudo == TipoConteudoMensagem.Outro),
            Arg.Any<CancellationToken>());
        await _queueService.DidNotReceive().EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, Arg.Any<ProcessarTurnoAgenteJob>());
        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task ConversaNovaAbertaPorBotaoAcaoNaoSaudaNemPublicaConversaAberta()
    {
        // Issue 1285: botão "acao:" de template (ex.: avaliação) numa conversa nova saía com a
        // saudação "já te respondo" e a automática de entrada, mas o agente nunca era enfileirado.
        await _useCase.ExecuteAsync(PayloadBotaoAcao("wamid.botao-novo", "acao:confirmar_endereco:123"));

        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        await _publicadorEventos.DidNotReceiveWithAnyArgs().PublicarAsync<ConversaAbertaEvent>(
            default, default!, default!, default, default!, default, default, default, default);
        await _queueService.DidNotReceive().EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, Arg.Any<ProcessarTurnoAgenteJob>());
    }

    [Fact]
    public async Task ConversaNovaPorTextoPublicaConversaAberta()
    {
        await _useCase.ExecuteAsync(PayloadTexto("wamid.texto-novo", "Oi"));

        await _publicadorEventos.Received(1).PublicarAsync(
            _empresaId, ConversaAbertaEvent.TipoEvento, "Conversa", Arg.Any<Guid>(), Arg.Any<ConversaAbertaEvent>(),
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
