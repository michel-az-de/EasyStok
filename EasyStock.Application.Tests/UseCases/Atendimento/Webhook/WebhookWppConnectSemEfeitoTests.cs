using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Observability;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Webhook;

/// <summary>
/// #3117: todo webhook da ponte respondido com 200 e sem efeito deixa o motivo em log e em contador,
/// sem telefone nem texto do cliente (#2590).
/// </summary>
public class WebhookWppConnectSemEfeitoTests
{
    private const string Telefone = "5511999999999";
    private const string Texto = "Quero um bolo de cenoura";
    private static readonly DateTimeOffset Captura = DateTimeOffset.FromUnixTimeSeconds(1793000000);

    private sealed class LoggerCapturado<T> : ILogger<T>
    {
        public List<string> Linhas { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Linhas.Add(formatter(state, exception) + exception);
    }

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly ITenantFeatureFlagRepository _flags = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IWebhookRecebidoRepository _recebidos = Substitute.For<IWebhookRecebidoRepository>();
    private readonly IIntegrationCredentialResolver _credenciais = Substitute.For<IIntegrationCredentialResolver>();
    private readonly IOperationalMetrics _metricas = Substitute.For<IOperationalMetrics>();
    private readonly LoggerCapturado<ProcessarWebhookWppConnectUseCase> _logPonte = new();
    private readonly LoggerCapturado<ProcessarEventoWhatsAppUseCase> _logEvento = new();
    private readonly ProcessarWebhookWppConnectUseCase _useCase;

    private string Sessao => CredencialWppConnect.NomeSessao(_empresaId);
    private IEnumerable<string> Linhas => _logPonte.Linhas.Concat(_logEvento.Linhas);

    public WebhookWppConnectSemEfeitoTests()
    {
        var empresa = Empresa.Criar("Casa da Baba", "11111111000191");
        empresa.Id = _empresaId;
        _empresas.GetByIdAsync(_empresaId).Returns(empresa);
        _flags.ListarAtivasAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(new[] { FeatureCatalogo.ModuloAtendimento });
        _credenciais.ObterAsync<CredencialWppConnect>(_empresaId, CredencialWppConnect.ProviderKey,
            AmbienteIntegracao.Production, Arg.Any<CancellationToken>()).Returns(new CredencialWppConnect(Sessao, "t", Captura));

        // Só o que os caminhos sem efeito tocam: nenhum deles chega a gravar, enfileirar ou responder.
        var tenant = Substitute.For<ITenantContextAccessor>();
        var evento = new ProcessarEventoWhatsAppUseCase(_empresas, _flags, null!, _conversas, _recebidos, null!, null!, null!,
            tenant, Substitute.For<IUnitOfWork>(), null!, null!, null!, null!, null!, null!, TimeProvider.System, _logEvento,
            metricas: _metricas);
        _useCase = new ProcessarWebhookWppConnectUseCase(_credenciais, tenant, Substitute.For<IWppConnectSessaoClient>(),
            evento, logger: _logPonte, metricas: _metricas);
    }

    private string Mensagem(string evento, string de, long quando, bool deMim = false) =>
        $$$"""{"event":"{{{evento}}}","session":"{{{Sessao}}}","id":"{{{(deMim ? "true" : "false")}}}_{{{de}}}_ABC","from":"{{{de}}}","to":"{{{de}}}","fromMe":{{{(deMim ? "true" : "false")}}},"type":"chat","body":"{{{Texto}}}","t":{{{quando}}}}""";

    private void DeveTerRegistrado(string motivo)
    {
        Linhas.Should().ContainSingle(l => l.Contains(motivo)).Which.Should().Contain(Sessao);
        Linhas.Should().NotContain(l => l.Contains(Telefone) || l.Contains(Texto));
        _metricas.Received(1).IncrementWebhookSemEfeito(motivo);
    }

    [Fact]
    public async Task Evento_fora_da_lista_registra_o_motivo_e_o_nome_do_evento()
    {
        var raw = $$$"""{"event":"status-find","session":"{{{Sessao}}}","status":"inChat"}""";

        (await _useCase.ExecuteAsync(raw)).Should().BeTrue();

        DeveTerRegistrado(WebhookSemEfeito.EventoIgnorado);
        Linhas.Should().Contain(l => l.Contains("status-find"));
    }

    [Fact]
    public async Task Nome_de_evento_que_parece_dado_nao_vai_para_o_log()
    {
        var raw = $$$"""{"event":"{{{Telefone}}}@c.us","session":"{{{Sessao}}}"}""";

        (await _useCase.ExecuteAsync(raw)).Should().BeTrue();

        DeveTerRegistrado(WebhookSemEfeito.EventoIgnorado);
    }

    [Fact]
    public async Task Grupo_registra_o_motivo_sem_telefone_nem_texto()
    {
        (await _useCase.ExecuteAsync(Mensagem("onmessage", "120363000000000000@g.us", 1793000100))).Should().BeTrue();

        DeveTerRegistrado(WebhookSemEfeito.Grupo);
    }

    /// <summary>
    /// Adendo da #3117: depois de Desconectar e Conectar, <c>InicioDaCaptura</c> anda para frente e o que chegou no
    /// intervalo volta como <c>unreadmessages</c> com data anterior. Decisão: não recuperar (o robô responderia a
    /// mensagem velha), mas o descarte deixa de ser calado.
    /// </summary>
    [Fact]
    public async Task Nao_lida_anterior_ao_inicio_da_captura_e_descartada_com_registro()
    {
        var raw = Mensagem("unreadmessages", $"{Telefone}@c.us", Captura.ToUnixTimeSeconds() - 60);

        (await _useCase.ExecuteAsync(raw)).Should().BeTrue();

        DeveTerRegistrado(WebhookSemEfeito.AnteriorACaptura);
        await _recebidos.DidNotReceiveWithAnyArgs().TryRegistrarAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Ack_sem_status_conhecido_registra_o_motivo()
    {
        var raw = $$$"""{"event":"onack","session":"{{{Sessao}}}","id":{"_serialized":"true_{{{Telefone}}}@c.us_ABC"},"ack":0}""";

        (await _useCase.ExecuteAsync(raw)).Should().BeTrue();

        DeveTerRegistrado(WebhookSemEfeito.AckDesconhecido);
    }

    [Fact]
    public async Task Duplicata_ja_processada_registra_o_motivo_sem_o_identificador_da_mensagem()
    {
        var anterior = WebhookRecebido.Criar("meta_whatsapp", "x", "h");
        anterior.MarcarProcessado(sucesso: true);
        _recebidos.TryRegistrarAsync(default!, default!, default!, default).ReturnsForAnyArgs((WebhookRecebido?)null);
        _recebidos.ObterAsync(default!, default!, default).ReturnsForAnyArgs(anterior);

        (await _useCase.ExecuteAsync(Mensagem("onmessage", $"{Telefone}@c.us", 1793000100))).Should().BeTrue();

        DeveTerRegistrado(WebhookSemEfeito.Duplicata);
    }

    [Fact]
    public async Task Mensagem_ja_gravada_pelo_identificador_registra_duplicata()
    {
        _recebidos.TryRegistrarAsync(default!, default!, default!, default)
            .ReturnsForAnyArgs(c => WebhookRecebido.Criar(c.ArgAt<string>(0), c.ArgAt<string>(1), c.ArgAt<string>(2)));
        _conversas.ObterMensagemPorExternoIdAsync(_empresaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(JaGravada());

        (await _useCase.ExecuteAsync(Mensagem("onmessage", $"{Telefone}@c.us", 1793000100))).Should().BeTrue();

        DeveTerRegistrado(WebhookSemEfeito.Duplicata);
    }

    [Fact]
    public async Task Eco_de_mensagem_ja_gravada_registra_o_motivo()
    {
        _conversas.ObterMensagemPorExternoIdAsync(_empresaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(JaGravada());

        (await _useCase.ExecuteAsync(Mensagem("onmessage", $"{Telefone}@c.us", 1793000100, deMim: true))).Should().BeTrue();

        DeveTerRegistrado(WebhookSemEfeito.Eco);
    }

    [Fact]
    public async Task Modulo_de_atendimento_desligado_registra_o_motivo()
    {
        _flags.ListarAtivasAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());

        (await _useCase.ExecuteAsync(Mensagem("onmessage", $"{Telefone}@c.us", 1793000100))).Should().BeTrue();

        DeveTerRegistrado(WebhookSemEfeito.ModuloDesligado);
    }

    [Fact]
    public async Task Mensagem_valida_nao_registra_descarte()
    {
        WppConnectWebhookParser.Parse(Mensagem("onmessage", $"{Telefone}@c.us", 1793000100)).Descarte.Should().BeNull();
        await Task.CompletedTask;
    }

    private Mensagem JaGravada() => EasyStock.Domain.Entities.Atendimento.Mensagem.Saida(_empresaId, Guid.NewGuid(),
        AutorMensagem.Dona, DateTime.UtcNow, TipoConteudoMensagem.Texto, "oi", "wpp:x:y");
}
