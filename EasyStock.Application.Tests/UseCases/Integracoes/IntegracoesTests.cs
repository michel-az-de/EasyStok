using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Tests.UseCases.Atendimento.Lembretes;
using EasyStock.Application.UseCases.Integracoes;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Integration;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Integracoes;

/// <summary>
/// F16 (#1246): botão Testar, vigia e a tela de Integrações sem segredo. Testadores, resolver e
/// chaves globais são falsos; nenhuma chamada sai para provedor.
/// </summary>
public class IntegracoesTests
{
    // Relativo ao relógio real: o domínio recusa ValidoAte no passado pelo DateTime.UtcNow.
    private static readonly DateTime Agora = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddHours(12), DateTimeKind.Utc);
    private const string Segredo = "APP_USR-segredo-de-teste-9876";

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly CredencialRepositoryEmMemoria _credenciais = new();
    private readonly IIntegrationCredentialResolver _resolver = Substitute.For<IIntegrationCredentialResolver>();
    private readonly IChavesGlobaisIntegracao _globais = Substitute.For<IChavesGlobaisIntegracao>();
    private readonly EstadoTesteIntegracaoEmMemoria _estadoGlobal = new();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly IAlvosVigiaIntegracoesQuery _alvos = Substitute.For<IAlvosVigiaIntegracoesQuery>();
    private readonly LembreteRepositoryEmMemoria _lembretes = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));
    private readonly TestadorFalso _mercadoPago = new(CatalogoIntegracoes.MercadoPago);

    public IntegracoesTests()
    {
        _alvos.ListarEmpresasAsync(Arg.Any<CancellationToken>()).Returns([_empresaId]);
        _globais.Obter(Arg.Any<string>()).Returns((IReadOnlyDictionary<string, string>?)null);
        _resolver.ObterAsync<Dictionary<string, string>>(_empresaId, CatalogoIntegracoes.MercadoPago, AmbienteIntegracao.Production, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string> { [CatalogoIntegracoes.CampoAccessToken] = Segredo });
    }

    private ExecutorTesteIntegracao Executor(params ITestadorIntegracao[] testadores) => new(
        testadores.Length == 0 ? [_mercadoPago] : testadores, _credenciais, _resolver, _globais, _estadoGlobal, _empresas, _uow, _relogio,
        NullLogger<ExecutorTesteIntegracao>.Instance);

    private VigiarIntegracoesUseCase Vigia() => new(_alvos, Executor(), _lembretes, _uow, _relogio,
        NullLogger<VigiarIntegracoesUseCase>.Instance);

    private CredencialIntegracao ChaveDaLoja(string provider = CatalogoIntegracoes.MercadoPago, DateTime? validoAte = null)
    {
        var c = CredencialIntegracao.Criar(_empresaId, CategoriaIntegracao.Payments, provider, AmbienteIntegracao.Production,
            [1, 2, 3], "kek-teste", new byte[12], new byte[16], Guid.NewGuid(), validoAte, mascara: "9876");
        _credenciais.Itens.Add(c);
        return c;
    }

    [Fact]
    public async Task ChaveInvalidaGeraFalhaELembreteEmAte15Min()
    {
        var chave = ChaveDaLoja();
        _mercadoPago.Proximo = ResultadoTesteIntegracao.Falhou("O Mercado Pago recusou o token (401).");

        var rodada = await Vigia().ExecuteAsync();

        rodada.Falhas.Should().Be(1);
        chave.UltimoTesteOk.Should().BeFalse();
        chave.UltimoTesteEm.Should().Be(Agora);
        var lembrete = _lembretes.Todos.Should().ContainSingle().Subject;
        lembrete.Tipo.Should().Be(TipoLembrete.IntegracaoParada);
        lembrete.Texto.Should().StartWith("Integração parada: Mercado Pago.").And.Contain("401").And.NotContain(Segredo);
        lembrete.VenceEm.Should().Be(Agora, "vence na hora: o avaliador S43 avisa a dona no minuto seguinte");

        // Próxima rodada, 15 min depois, ainda quebrada: não repete o lembrete.
        _relogio.Advance(VigiarIntegracoesUseCase.Intervalo);
        await Vigia().ExecuteAsync();
        _lembretes.Todos.Should().ContainSingle();

        // A dona trocou a chave: o teste passa e o lembrete some sozinho.
        _relogio.Advance(VigiarIntegracoesUseCase.Intervalo);
        _mercadoPago.Proximo = ResultadoTesteIntegracao.Passou("Conta conferida.");
        var depois = await Vigia().ExecuteAsync();
        depois.LembretesResolvidos.Should().Be(1);
        lembrete.EstaAberto.Should().BeFalse();
        chave.UltimoTesteOk.Should().BeTrue();
    }

    [Fact]
    public async Task ChaveVencendoEm7DiasGeraLembreteUmaVez()
    {
        ChaveDaLoja(validoAte: Agora.AddDays(5));
        _mercadoPago.Proximo = ResultadoTesteIntegracao.Passou("ok");

        await Vigia().ExecuteAsync();
        _relogio.Advance(VigiarIntegracoesUseCase.Intervalo);
        await Vigia().ExecuteAsync();

        var lembrete = _lembretes.Todos.Should().ContainSingle().Subject;
        lembrete.Tipo.Should().Be(TipoLembrete.IntegracaoVencendo);
        lembrete.Texto.Should().Contain(Agora.AddDays(5).ToString("dd/MM/yyyy"));
    }

    [Fact]
    public async Task SemChaveDaLojaUsaAGlobalEGuardaOEstadoNaMemoria()
    {
        _globais.Obter(CatalogoIntegracoes.MercadoPago)
            .Returns(new Dictionary<string, string> { [CatalogoIntegracoes.CampoAccessToken] = "global-token-0000" });
        _mercadoPago.Proximo = ResultadoTesteIntegracao.Falhou("Token global recusado.");

        var teste = await Executor().TestarAsync(_empresaId, CatalogoIntegracoes.MercadoPago);

        teste!.Origem.Should().Be(OrigemChaveIntegracao.Global);
        _mercadoPago.UltimaChave!.Campo(CatalogoIntegracoes.CampoAccessToken).Should().Be("global-token-0000");
        _estadoGlobal.Obter(_empresaId, CatalogoIntegracoes.MercadoPago)!.Ok.Should().BeFalse();
    }

    [Fact]
    public async Task ProvedorQueNaoRespondeEm5SegundosViraFalha()
    {
        ChaveDaLoja();
        var lento = new TestadorFalso(CatalogoIntegracoes.MercadoPago) { Travar = true };

        var execucao = Executor(lento).TestarAsync(_empresaId, CatalogoIntegracoes.MercadoPago);
        await lento.Iniciou.Task;
        _relogio.Advance(ExecutorTesteIntegracao.Teto);
        var teste = await execucao;

        teste!.Ok.Should().BeFalse();
        teste.Mensagem.Should().Be("O provedor não respondeu em 5 s.");
    }

    [Fact]
    public async Task ListarNuncaDevolveOSegredo()
    {
        var chave = ChaveDaLoja();
        chave.RegistrarTeste(Agora, ok: false, "Token recusado.");

        var lista = await new ListarIntegracoesUseCase(_credenciais, _globais, _estadoGlobal, _empresas, _relogio).ExecuteAsync(_empresaId);

        lista.Select(i => i.Provider).Should().Equal("mercadopago", "whatsapp", "googlemaps", "lalamove");
        var mp = lista[0];
        mp.Origem.Should().Be("loja");
        mp.Mascara.Should().Be("9876");
        mp.Alerta.Should().Be("parada", "é o que acende a faixa vermelha do console");
        System.Text.Json.JsonSerializer.Serialize(lista).Should().NotContain(Segredo);
        lista[1].LojaGrava.Should().BeFalse("WhatsApp é gerido pela FMA");
    }

    [Fact]
    public async Task SalvarWhatsAppEhRecusado()
    {
        var act = () => new SalvarChaveIntegracaoUseCase(_credenciais, _resolver).ExecuteAsync(new SalvarChaveIntegracaoCommand(
            _empresaId, Guid.NewGuid(), CatalogoIntegracoes.WhatsApp,
            new Dictionary<string, string?> { ["phoneNumberId"] = "123" }, null, null));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*gerido pela FMA*");
    }

    [Fact]
    public async Task SalvarLalamoveExigeAmbienteECifraComMascara()
    {
        var useCase = new SalvarChaveIntegracaoUseCase(_credenciais, _resolver);
        var campos = new Dictionary<string, string?> { ["apiKey"] = "pk_test_abcdef1234", ["apiSecret"] = "sk_test_segredo" };

        var semAmbiente = () => useCase.ExecuteAsync(new SalvarChaveIntegracaoCommand(_empresaId, Guid.NewGuid(), "lalamove", campos, null, null));
        await semAmbiente.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*ambiente*");

        await useCase.ExecuteAsync(new SalvarChaveIntegracaoCommand(_empresaId, Guid.NewGuid(), "lalamove", campos, "sandbox", null));

        await _resolver.Received(1).SalvarAsync(_empresaId, CategoriaIntegracao.Logistics, "lalamove", AmbienteIntegracao.Sandbox,
            Arg.Is<Dictionary<string, string>>(d => d["apiSecret"] == "sk_test_segredo"), Arg.Any<Guid>(), null, "1234", Arg.Any<CancellationToken>());
    }

    private sealed class TestadorFalso(string provider) : ITestadorIntegracao
    {
        public string Provider => provider;
        public ResultadoTesteIntegracao Proximo { get; set; } = ResultadoTesteIntegracao.Passou("ok");
        public ChaveParaTeste? UltimaChave { get; private set; }
        public bool Travar { get; init; }
        public TaskCompletionSource Iniciou { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ResultadoTesteIntegracao> TestarAsync(ChaveParaTeste chave, CancellationToken ct = default)
        {
            UltimaChave = chave;
            Iniciou.TrySetResult();
            if (Travar) await Task.Delay(Timeout.Infinite, ct);
            return Proximo;
        }
    }

    private sealed class CredencialRepositoryEmMemoria : ICredencialIntegracaoRepository
    {
        public List<CredencialIntegracao> Itens { get; } = [];

        public Task<CredencialIntegracao?> GetAtivaAsync(Guid empresaId, string providerKey, AmbienteIntegracao ambiente, CancellationToken ct = default) =>
            Task.FromResult(Itens.FirstOrDefault(c => c.EmpresaId == empresaId && c.ProviderKey == providerKey && c.Ambiente == ambiente && c.Ativo));

        public Task<CredencialIntegracao?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Itens.FirstOrDefault(c => c.Id == id));

        public Task<IReadOnlyList<CredencialIntegracao>> ListarPorEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CredencialIntegracao>>(Itens.Where(c => c.EmpresaId == empresaId).ToList());

        public Task<IReadOnlyList<CredencialIntegracao>> ListarPorKekAsync(string kekId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CredencialIntegracao>>(Itens.Where(c => c.KekId == kekId).ToList());

        public Task<IReadOnlyList<CredencialIntegracao>> ListarAtivasDoProviderAsync(Guid empresaId, string providerKey, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CredencialIntegracao>>(Itens.Where(c => c.EmpresaId == empresaId && c.ProviderKey == providerKey && c.Ativo).ToList());

        public Task<IReadOnlyList<CredencialIntegracao>> ListarAtivasDaEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CredencialIntegracao>>(Itens.Where(c => c.EmpresaId == empresaId && c.Ativo).ToList());

        public Task AddAsync(CredencialIntegracao credencial, CancellationToken ct = default)
        {
            Itens.Add(credencial);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(CredencialIntegracao credencial, CancellationToken ct = default) => Task.CompletedTask;
    }
}
