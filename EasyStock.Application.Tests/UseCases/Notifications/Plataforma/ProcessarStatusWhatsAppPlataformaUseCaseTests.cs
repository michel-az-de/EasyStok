using System.Diagnostics.Metrics;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Notifications.Plataforma;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Notifications.Plataforma;

/// <summary>N6: o status do número de plataforma fecha o outbox pelo opaco, monotonicamente, sem telefone no log.</summary>
public class ProcessarStatusWhatsAppPlataformaUseCaseTests
{
    private const string NumeroPlataforma = "7770009999";
    private const string Telefone = "+5511999990001";

    private readonly IOutboxNotificacaoRepository _outbox = Substitute.For<IOutboxNotificacaoRepository>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly LoggerQueGuarda _log = new();
    private readonly Guid _empresaId = Guid.NewGuid();

    private ProcessarStatusWhatsAppPlataformaUseCase Sut() => new(
        _outbox, _tenant, _uow,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:WhatsApp:Plataforma:PhoneNumberId"] = NumeroPlataforma
        }).Build(),
        _log);

    private OutboxMensagemNotificacao Mensagem(StatusOutbox status, string? wamid = null)
    {
        var m = OutboxMensagemNotificacao.Criar(
            Guid.NewGuid(), Guid.NewGuid(), _empresaId, CanalNotificacao.WhatsApp, Telefone, "", "corpo",
            CategoriaConteudoNotificacao.Operacional, remetente: OrigemRemetente.Plataforma,
            metadadosJson: """{"template":"prazo_estourado"}""");
        switch (status)
        {
            case StatusOutbox.EmEnvio: m.MarcarEmEnvio(); break;
            case StatusOutbox.Indeterminado: m.MarcarIndeterminado("timeout", "meta-plataforma"); break;
            case StatusOutbox.Enviado: m.MarcarEnviado("meta-plataforma"); break;
            case StatusOutbox.Falhado: m.MarcarFalhaTentativa("erro", TimeSpan.Zero, permanente: true); break;
        }

        m.RegistrarProviderMensagemId(wamid);
        _outbox.ObterAsync(_empresaId, m.Id, Arg.Any<CancellationToken>()).Returns(m);
        return m;
    }

    private StatusPlataforma Status(OutboxMensagemNotificacao m, string status, int? codigo = null,
        string? categoriaPreco = null, string? phone = NumeroPlataforma, string? opaco = "")
    {
        opaco = opaco == "" ? $"{m.EmpresaId:N}.{m.Id:N}" : opaco;
        return new StatusPlataforma(phone!, "wamid.PLAT1", status, opaco, codigo, codigo is null ? null : "falha", categoriaPreco);
    }

    [Theory]
    [InlineData(StatusOutbox.EmEnvio, "sent")]
    [InlineData(StatusOutbox.Indeterminado, "sent")]
    [InlineData(StatusOutbox.Indeterminado, "delivered")]
    [InlineData(StatusOutbox.Indeterminado, "read")]
    public async Task SentConfirmaOIndeterminadoEGuardaOProviderMensagemId(StatusOutbox antes, string status)
    {
        var m = Mensagem(antes);

        var ok = await Sut().ExecuteAsync(Status(m, status));

        ok.Should().BeTrue();
        m.Status.Should().Be(StatusOutbox.Enviado);
        m.ProviderMensagemId.Should().Be("wamid.PLAT1");
        _tenant.Received().SetCurrentTenant(_empresaId);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task FailedMarcaFalhadoComCodigoSemTelefoneNoLog()
    {
        var m = Mensagem(StatusOutbox.Indeterminado);

        await Sut().ExecuteAsync(Status(m, "failed", codigo: 131026));

        m.Status.Should().Be(StatusOutbox.Falhado);
        m.ErroUltimaTentativa.Should().Contain("131026");
        _log.Texto.Should().Contain("131026");
        _log.Texto.Should().NotContain(Telefone).And.NotContain("5511999990001");
    }

    [Fact]
    public async Task StatusRepetidoOuForaDeOrdemNaoRegride()
    {
        var enviado = Mensagem(StatusOutbox.Enviado, "wamid.PLAT1");
        var falhado = Mensagem(StatusOutbox.Falhado);
        var emEnvio = Mensagem(StatusOutbox.EmEnvio);

        await Sut().ExecuteAsync(Status(enviado, "sent"));      // repetido
        await Sut().ExecuteAsync(Status(enviado, "delivered")); // delivered depois de sent
        await Sut().ExecuteAsync(Status(falhado, "delivered")); // sucesso depois da falha: não regride
        await Sut().ExecuteAsync(Status(emEnvio, "failed", 131026)); // failed antes do dispatcher: espera o lease

        enviado.Status.Should().Be(StatusOutbox.Enviado);
        falhado.Status.Should().Be(StatusOutbox.Falhado);
        emEnvio.Status.Should().Be(StatusOutbox.EmEnvio);
    }

    [Fact]
    public async Task PhoneNumberIdDeOutroNumeroEhIgnorado()
    {
        var m = Mensagem(StatusOutbox.Indeterminado);

        var ok = await Sut().ExecuteAsync(Status(m, "sent", phone: "1112223334"));

        ok.Should().BeTrue();
        m.Status.Should().Be(StatusOutbox.Indeterminado);
        await _outbox.DidNotReceiveWithAnyArgs().ObterAsync(default, default, default);
        _log.Texto.Should().Contain("1112223334");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("naoguid.naoguid")]
    public async Task SemOpacoEhIgnoradoSemErro(string? opaco)
    {
        var m = Mensagem(StatusOutbox.Indeterminado);

        var ok = await Sut().ExecuteAsync(new StatusPlataforma(NumeroPlataforma, "wamid.X", "sent", opaco, null, null, null));

        ok.Should().BeTrue();
        m.Status.Should().Be(StatusOutbox.Indeterminado);
        await _outbox.DidNotReceiveWithAnyArgs().ObterAsync(default, default, default);
    }

    [Fact]
    public async Task OpacoDeOutraEmpresaNaoAchaALinha()
    {
        var m = Mensagem(StatusOutbox.Indeterminado);
        var outraEmpresa = Guid.NewGuid();
        _outbox.ObterAsync(outraEmpresa, m.Id, Arg.Any<CancellationToken>()).Returns((OutboxMensagemNotificacao?)null);

        var ok = await Sut().ExecuteAsync(Status(m, "sent", opaco: $"{outraEmpresa:N}.{m.Id:N}"));

        ok.Should().BeTrue();
        m.Status.Should().Be(StatusOutbox.Indeterminado);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task FalhaDeBancoDevolveFalsoParaAMetaReenviar()
    {
        var m = Mensagem(StatusOutbox.Indeterminado);
        _uow.CommitAsync().Returns<int>(_ => throw new InvalidOperationException("db"));

        var ok = await Sut().ExecuteAsync(Status(m, "sent"));

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task PrecoComoMarketingSomaContador()
    {
        var m = Mensagem(StatusOutbox.Indeterminado);
        long soma = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i, l) =>
        {
            if (i.Name == "notifications.whatsapp.plataforma.cobrado_como_marketing") l.EnableMeasurementEvents(i);
        };
        listener.SetMeasurementEventCallback<long>((_, v, _, _) => Interlocked.Add(ref soma, v));
        listener.Start();

        await Sut().ExecuteAsync(Status(m, "sent", categoriaPreco: "marketing"));
        await Sut().ExecuteAsync(Status(m, "delivered", categoriaPreco: "utility"));

        soma.Should().Be(1);
        _log.Texto.Should().Contain("prazo_estourado");
    }

    private sealed class LoggerQueGuarda : ILogger<ProcessarStatusWhatsAppPlataformaUseCase>
    {
        private readonly List<string> _linhas = [];
        public string Texto => string.Join('\n', _linhas);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _linhas.Add(formatter(state, exception));
    }
}
