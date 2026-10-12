using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.ConfirmEmail;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>N4: o clique no link confirma o e-mail; com troca pendente, confirma o endereço novo e só então o Email muda.</summary>
public class ConfirmEmailUseCaseTests
{
    private readonly IEmailConfirmationTokenRepository _tokens = Substitute.For<IEmailConfirmationTokenRepository>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IAuditLogRepository _auditoria = Substitute.For<IAuditLogRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly Usuario _ana;
    private readonly EmailConfirmationToken _token;

    public ConfirmEmailUseCaseTests()
    {
        _ana = Usuario.Criar("Ana", "ana@casadababa.com", "hash");
        _ana.EmailConfirmado = true;
        _usuarios.GetByIdAsync(_ana.Id).Returns(_ana);
        _token = EmailConfirmationToken.Criar(_ana.Id, "hash-do-token", null, null);
        _tokens.GetByTokenAsync("token-do-email").Returns(_token);
    }

    private ConfirmEmailUseCase Criar() => new(
        _tokens, _usuarios, _auditoria,
        new RevogadorSessoes(_usuarios, _refreshTokens, Substitute.For<ICacheService>(), TimeProvider.System,
            Substitute.For<ILogger<RevogadorSessoes>>()),
        _unitOfWork, Substitute.For<ILogger<ConfirmEmailUseCase>>());

    [Fact]
    public async Task ConfirmarComEmailPendenteTrocaOEnderecoEMantemOLogin()
    {
        _ana.SolicitarTrocaDeEmail("nova@casadababa.com");

        var r = await Criar().ExecuteAsync(new ConfirmEmailCommand("token-do-email"));

        r.Sucesso.Should().BeTrue();
        _ana.Email.Should().Be("nova@casadababa.com");
        _ana.EmailPendente.Should().BeNull();
        _ana.EmailConfirmado.Should().BeTrue("o login segue possível, agora pelo endereço novo");
        _token.Confirmado.Should().BeTrue();
        await _auditoria.Received(1).AddAsync(Arg.Is<AuditLog>(a => a.Acao == "email-alterado"));
        _unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task ConfirmarSemPendenciaSoMarcaOEmailComoConfirmadoEMantemAsSessoes()
    {
        _ana.EmailConfirmado = false;

        await Criar().ExecuteAsync(new ConfirmEmailCommand("token-do-email"));

        _ana.EmailConfirmado.Should().BeTrue();
        _ana.Email.Should().Be("ana@casadababa.com");
        _ana.SessoesValidasDesde.Should().BeNull("confirmar o primeiro e-mail não derruba sessão");
        await _auditoria.Received(1).AddAsync(Arg.Is<AuditLog>(a => a.Acao == "email-confirmado"));
    }

    [Fact]
    public async Task EnderecoTomadoNoIntervaloRecusaESegueNoEmailAntigo()
    {
        _ana.SolicitarTrocaDeEmail("nova@casadababa.com");
        _usuarios.GetByEmailAsync("nova@casadababa.com").Returns(Usuario.Criar("Leo", "nova@casadababa.com", "hash"));

        var acao = () => Criar().ExecuteAsync(new ConfirmEmailCommand("token-do-email"));

        await acao.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*ja cadastrado*");
        _ana.Email.Should().Be("ana@casadababa.com");
        _ana.EmailPendente.Should().Be("nova@casadababa.com");
        _unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task TokenInvalidoOuExpiradoRecusa()
    {
        _tokens.GetByTokenAsync("ruim").Returns((EmailConfirmationToken?)null);

        var acao = () => Criar().ExecuteAsync(new ConfirmEmailCommand("ruim"));

        await acao.Should().ThrowAsync<RegraDeDominioVioladaException>();
        _ana.EmailPendente.Should().BeNull();
    }
}
