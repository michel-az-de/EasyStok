using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Atendimento.Email;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Email;

/// <summary>
/// #1432: uma rodada na caixa. Marca como lido só depois de gravar; o que falhou fica não lido para a próxima; sem
/// módulo de atendimento ou sem caixa, nem conecta.
/// </summary>
public class LerCaixaEmailAtendimentoUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 10, 7, 13, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IIntegrationCredentialResolver _credenciais = Substitute.For<IIntegrationCredentialResolver>();
    private readonly ITenantFeatureFlagRepository _flags = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly ICaixaEmailCliente _cliente = Substitute.For<ICaixaEmailCliente>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly SessaoDeTeste _sessao = new();

    private static readonly CaixaEmailAtendimento Caixa = new(
        "contato@casadababa.com", "Casa da Baba", "imap.hostinger.com", 993, "smtp.hostinger.com", 465,
        "contato@casadababa.com", "segredo-de-teste", Agora);

    public LerCaixaEmailAtendimentoUseCaseTests()
    {
        _flags.ListarAtivasAsync(_empresaId, Arg.Any<CancellationToken>())
            .Returns([FeatureCatalogo.ModuloAtendimento]);
        _credenciais.ObterAsync<CaixaEmailAtendimento>(_empresaId, CaixaEmailAtendimento.ProviderKey,
            AmbienteIntegracao.Production, Arg.Any<CancellationToken>()).Returns(Caixa);
        _cliente.AbrirAsync(Caixa, Arg.Any<CancellationToken>()).Returns(_sessao);
    }

    private LerCaixaEmailAtendimentoUseCase Sut()
    {
        var receber = new ReceberEmailAtendimentoUseCase(
            _conversas, Substitute.For<IEmailAtendimentoQuery>(), Substitute.For<IFileStorage>(),
            Substitute.For<IOperacaoEventPublisher>(), _uow, new FakeTimeProvider(Agora),
            NullLogger<ReceberEmailAtendimentoUseCase>.Instance);
        return new LerCaixaEmailAtendimentoUseCase(_credenciais, _flags, _cliente, receber, _tenant,
            NullLogger<LerCaixaEmailAtendimentoUseCase>.Instance);
    }

    private static EmailRecebido Email(string id, bool autoGerado = false) =>
        new(id, $"msg-{id}@x", "maria@exemplo.com", "Maria", "Bolo", "Oi", Agora, autoGerado, []);

    [Fact]
    public async Task GravaEMarcaComoLido_NaOrdem_EFixaOTenant()
    {
        _sessao.Emails.Add(Email("1"));
        _sessao.Emails.Add(Email("2", autoGerado: true));

        var r = await Sut().ExecuteAsync(_empresaId, 20);

        r.Should().Be(new LeituraCaixaEmailResultado(Gravados: 1, Duplicados: 0, Ignorados: 1, Falhas: 0));
        _sessao.Lidos.Should().Equal("1", "2");
        _sessao.Descartada.Should().BeTrue();
        _tenant.Received(1).SetCurrentTenant(_empresaId);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task FalhaAoGravar_DeixaNaoLido_ESegueParaOProximo()
    {
        _sessao.Emails.Add(Email("1"));
        _sessao.Emails.Add(Email("2"));
        _uow.CommitAsync().Returns(Task.FromException<int>(new InvalidOperationException("banco fora")), Task.FromResult(1));

        var r = await Sut().ExecuteAsync(_empresaId, 20);

        r.Falhas.Should().Be(1);
        r.Gravados.Should().Be(1);
        _sessao.Lidos.Should().Equal("2");
    }

    [Fact]
    public async Task SemModuloDeAtendimento_OuSemCaixa_NaoConecta()
    {
        _flags.ListarAtivasAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(new List<string>());
        (await Sut().ExecuteAsync(_empresaId, 20)).Should().Be(LeituraCaixaEmailResultado.Nada);

        _flags.ListarAtivasAsync(_empresaId, Arg.Any<CancellationToken>()).Returns([FeatureCatalogo.ModuloAtendimento]);
        _credenciais.ObterAsync<CaixaEmailAtendimento>(_empresaId, CaixaEmailAtendimento.ProviderKey,
            AmbienteIntegracao.Production, Arg.Any<CancellationToken>()).Returns((CaixaEmailAtendimento?)null);
        (await Sut().ExecuteAsync(_empresaId, 20)).Should().Be(LeituraCaixaEmailResultado.Nada);

        await _cliente.DidNotReceive().AbrirAsync(Arg.Any<CaixaEmailAtendimento>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RespeitaOLote()
    {
        for (var i = 1; i <= 5; i++) _sessao.Emails.Add(Email(i.ToString()));

        await Sut().ExecuteAsync(_empresaId, 3);

        _sessao.Lidos.Should().Equal("1", "2", "3");
    }

    /// <summary>INBOX em memória: o que foi marcado como lido, e em que ordem.</summary>
    private sealed class SessaoDeTeste : ISessaoCaixaEmail
    {
        public List<EmailRecebido> Emails { get; } = [];
        public List<string> Lidos { get; } = [];
        public bool Descartada { get; private set; }

        public Task<IReadOnlyList<string>> ListarNaoLidosAsync(int maximo, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Emails.Select(e => e.IdNaCaixa).Except(Lidos).Take(maximo).ToList());

        public Task<EmailRecebido> BaixarAsync(string idNaCaixa, CancellationToken ct = default) =>
            Task.FromResult(Emails.Single(e => e.IdNaCaixa == idNaCaixa));

        public Task MarcarLidoAsync(string idNaCaixa, CancellationToken ct = default)
        {
            Lidos.Add(idNaCaixa);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Descartada = true;
            return ValueTask.CompletedTask;
        }
    }
}
