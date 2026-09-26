using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ProcessarEventoWhatsAppUseCase _useCase;

    public ProcessarEventoWhatsAppUseCaseTests()
    {
        _useCase = new ProcessarEventoWhatsAppUseCase(
            _empresaRepository, _featureFlagRepository, _configuracaoRepository, _conversaRepository,
            _webhookRecebidoRepository, _cloudClient, _queueService, _eventPublisher, _tenantContext,
            _unitOfWork, NullLogger<ProcessarEventoWhatsAppUseCase>.Instance);

        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");
        empresa.Id = _empresaId;
        _empresaRepository.GetByWhatsAppPhoneNumberIdAsync(PhoneNumberId, Arg.Any<CancellationToken>()).Returns(empresa);
        _featureFlagRepository.ListarAtivasAsync(_empresaId, Arg.Any<CancellationToken>())
            .Returns(new[] { FeatureCatalogo.ModuloAtendimento });
        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns((ConfiguracaoAtendimento?)null);
        _conversaRepository.ObterAbertaPorContatoAsync(_empresaId, ContatoWaId, Arg.Any<CancellationToken>())
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
}
