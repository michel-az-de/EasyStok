using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>N4: trocar o e-mail da conta pede a senha atual, vira duas etapas e avisa o endereço antigo.</summary>
public class AtualizarUsuarioAtualUseCaseTests
{
    private readonly TrocaDeContatoFixture _f = new(new()
    {
        ["Auth:TrustedLinkOrigins:0"] = "https://app.easystok.com.br"
    });
    private readonly Usuario _ana;

    public AtualizarUsuarioAtualUseCaseTests()
    {
        _ana = _f.NovoUsuario();
        _f.UsuarioAtual.UsuarioId.Returns(_ana.Id);
    }

    private AtualizarUsuarioAtualUseCase Criar() =>
        new(_f.Usuarios, _f.UsuarioAtual, _f.UnitOfWork, _f.Servico, Substitute.For<ILogger<AtualizarUsuarioAtualUseCase>>());

    private static AtualizarUsuarioAtualCommand Troca(string email, string? senha = TrocaDeContatoFixture.SenhaCerta) =>
        new(null, email, SenhaAtual: senha, BaseUrl: "https://app.easystok.com.br");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TrocaDeEmailSemSenhaAtualRecusaComForbidden(string? senha)
    {
        var acao = () => Criar().ExecuteAsync(Troca("nova@casadababa.com", senha));

        await acao.Should().ThrowAsync<UsuarioNaoAutorizadoException>();
        _ana.Email.Should().Be("ana@casadababa.com");
        _ana.EmailPendente.Should().BeNull();
        _f.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task SenhaAtualErradaRecusaComForbiddenEContaFalhaDeLogin()
    {
        var acao = () => Criar().ExecuteAsync(Troca("nova@casadababa.com", "SenhaErrada@999"));

        await acao.Should().ThrowAsync<UsuarioNaoAutorizadoException>("403, não 401: o console entende 401 como sessão vencida");
        _ana.FailedLoginAttempts.Should().Be(1);
        _ana.EmailPendente.Should().BeNull();
        await _f.Usuarios.Received(1).UpdateAsync(_ana);
        _f.UnitOfWork.CommitCount.Should().Be(1, "a falha é gravada antes de recusar");
    }

    [Fact]
    public async Task CincoSenhasErradasBloqueiamAContaPor15Minutos()
    {
        for (var i = 0; i < Usuario.FalhasParaBloquear; i++)
            await Assert.ThrowsAsync<UsuarioNaoAutorizadoException>(
                () => Criar().ExecuteAsync(Troca("nova@casadababa.com", "SenhaErrada@999")));

        _ana.EstaBloqueado().Should().BeTrue();

        // Bloqueada, nem a senha certa troca o e-mail.
        await Assert.ThrowsAsync<UsuarioNaoAutorizadoException>(() => Criar().ExecuteAsync(Troca("nova@casadababa.com")));
        _ana.EmailPendente.Should().BeNull();
    }

    [Fact]
    public async Task ComSenhaGravaEmailPendenteSemTrocarOEmail()
    {
        var r = await Criar().ExecuteAsync(Troca("  Nova@CasaDaBaba.com "));

        _ana.Email.Should().Be("ana@casadababa.com");
        _ana.EmailConfirmado.Should().BeTrue("o login segue possível pelo endereço antigo");
        _ana.EmailPendente.Should().Be("Nova@CasaDaBaba.com");
        r.Email.Should().Be("ana@casadababa.com");
        r.EmailPendente.Should().Be("Nova@CasaDaBaba.com");
        await _f.Usuarios.Received().UpdateAsync(_ana);
    }

    [Fact]
    public async Task EnfileiraConfirmacaoNoEnderecoNovoEAvisoMascaradoNoAntigo()
    {
        await Criar().ExecuteAsync(Troca("nova@casadababa.com"));

        _f.Eventos.Should().HaveCount(2);
        var confirmacao = _f.Eventos.Single(e => e.Tipo == TipoEventoNotificacao.ConfirmacaoEmail);
        confirmacao.EmpresaId.Should().Be(_f.EmpresaId);
        confirmacao.Payload["email"].GetString().Should().Be("nova@casadababa.com");
        confirmacao.Payload["link_confirmacao"].GetString().Should().StartWith("https://app.easystok.com.br/auth/confirmar-email?token=");
        confirmacao.Payload["expira_em_horas"].GetInt32().Should().Be(24);

        var aviso = _f.Eventos.Single(e => e.Tipo == TipoEventoNotificacao.ContatoAlterado);
        aviso.Payload["email"].GetString().Should().Be("ana@casadababa.com");
        aviso.Payload["contato"].GetString().Should().Be("e-mail");
        aviso.Payload["novo_mascarado"].GetString().Should().Be("n***@casadababa.com");
        aviso.Payload.Values.Select(v => v.ToString()).Should().NotContain(v => v.Contains("nova@casadababa.com") && !v.Contains("token="),
            "o aviso nunca revela o endereço novo inteiro");
        aviso.Payload["quando"].GetString().Should().Be("02/10/2026 14:30", "horário de Brasília");
    }

    [Fact]
    public async Task BaseUrlForaDaAllowlistDegradaParaTokenPuro()
    {
        await Criar().ExecuteAsync(new AtualizarUsuarioAtualCommand(
            null, "nova@casadababa.com", SenhaAtual: TrocaDeContatoFixture.SenhaCerta, BaseUrl: "https://evil.example"));

        var link = _f.Eventos.Single(e => e.Tipo == TipoEventoNotificacao.ConfirmacaoEmail).Payload["link_confirmacao"].GetString();
        link.Should().NotContain("evil.example").And.NotContain("http");
    }

    [Fact]
    public async Task EmailJaUsadoPorOutroUsuarioRecusa()
    {
        var outro = Usuario.Criar("Leo", "leo@casadababa.com", "hash");
        _f.Usuarios.GetByEmailAsync("leo@casadababa.com").Returns(outro);

        var acao = () => Criar().ExecuteAsync(Troca("leo@casadababa.com"));

        await acao.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*ja cadastrado*");
        _ana.EmailPendente.Should().BeNull();
        _f.Eventos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sem-arroba")]
    [InlineData("a@")]
    [InlineData("@casadababa.com")]
    public async Task EmailInvalidoRecusa(string email)
    {
        var acao = () => Criar().ExecuteAsync(Troca(email));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        _ana.EmailPendente.Should().BeNull();
    }

    [Fact]
    public async Task NovoPedidoInvalidaOsTokensAnteriores()
    {
        await Criar().ExecuteAsync(Troca("primeira@casadababa.com"));
        await Criar().ExecuteAsync(Troca("segunda@casadababa.com"));

        await _f.Tokens.Received(2).DeleteAllByUsuarioIdAsync(_ana.Id);
        _f.TokensGravados.Should().HaveCount(2);
        _f.TokensGravados.Select(t => t.TokenHash).Distinct().Should().HaveCount(2);
        _ana.EmailPendente.Should().Be("segunda@casadababa.com", "o link só vale para o pendente mais recente");
    }

    [Fact]
    public async Task NomeETemaSemTrocarOEmailNaoPedemSenha()
    {
        var r = await Criar().ExecuteAsync(new AtualizarUsuarioAtualCommand("Ana Maria", null, "dark"));

        r.Nome.Should().Be("Ana Maria");
        r.TemaPreferido.Should().Be("dark");
        _f.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task OMesmoEmailNaoPedeSenhaNemEnfileiraNada()
    {
        await Criar().ExecuteAsync(new AtualizarUsuarioAtualCommand(null, "ANA@casadababa.com"));

        _ana.EmailPendente.Should().BeNull();
        _f.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task SuperAdminSemEmpresaNoTokenUsaAEmpresaPadraoEFixaOTenant()
    {
        var padrao = Guid.NewGuid();
        _f.UsuarioAtual.EmpresaId.Returns(Guid.Empty);
        _f.EmpresaPadrao.ResolverAsync(Arg.Any<CancellationToken>()).Returns(padrao);

        await Criar().ExecuteAsync(Troca("nova@casadababa.com"));

        _f.Eventos.Should().OnlyContain(e => e.EmpresaId == padrao);
        _f.Tenant.Received(1).SetCurrentTenant(padrao);
    }

    [Fact]
    public async Task SemEmpresaNemPadraoRecusaAntesDeMudarQualquerCoisa()
    {
        _f.UsuarioAtual.EmpresaId.Returns(Guid.Empty);
        _f.EmpresaPadrao.ResolverAsync(Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var acao = () => Criar().ExecuteAsync(Troca("nova@casadababa.com"));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("EMPRESA_PADRAO_NAO_RESOLVIDA");
        _ana.EmailPendente.Should().BeNull();
    }

    [Fact]
    public async Task TelefoneNovoExigeSenhaEZeraAVerificacao()
    {
        _ana.DefinirTelefone(TelefoneE164.From("11997573992"));
        _ana.MarcarTelefoneVerificado(DateTime.UtcNow);
        var useCase = new EasyStock.Application.UseCases.ContatoUsuario.DefinirMeuTelefoneUseCase(
            _f.Usuarios, _f.UsuarioAtual, _f.Servico, RevogadorDe(_f), _f.UnitOfWork,
            Substitute.For<ILogger<EasyStock.Application.UseCases.ContatoUsuario.DefinirMeuTelefoneUseCase>>());

        // Sem a senha atual, nada muda.
        await Assert.ThrowsAsync<UsuarioNaoAutorizadoException>(() => useCase.ExecuteAsync(
            new EasyStock.Application.UseCases.ContatoUsuario.DefinirMeuTelefoneCommand("11988887777", null)));
        _ana.Telefone!.Value.Should().Be("+5511997573992");
        _ana.TelefoneVerificadoEm.Should().NotBeNull();

        var r = await useCase.ExecuteAsync(
            new EasyStock.Application.UseCases.ContatoUsuario.DefinirMeuTelefoneCommand("(11) 98888-7777", TrocaDeContatoFixture.SenhaCerta));

        r.Telefone.Should().Be("+5511988887777");
        r.Verificado.Should().BeFalse();
        _ana.TelefoneVerificadoEm.Should().BeNull("o WhatsApp não recebe até a verificação");
        var aviso = _f.Eventos.Should().ContainSingle().Subject;
        aviso.Tipo.Should().Be(TipoEventoNotificacao.ContatoAlterado);
        aviso.Payload["email"].GetString().Should().Be("ana@casadababa.com");
        aviso.Payload["contato"].GetString().Should().Be("telefone");
        aviso.Payload["novo_mascarado"].GetString().Should().Be("+55 11 *****-7777");
    }

    [Fact]
    public async Task TelefoneInvalidoRecusaCom400()
    {
        var useCase = new EasyStock.Application.UseCases.ContatoUsuario.DefinirMeuTelefoneUseCase(
            _f.Usuarios, _f.UsuarioAtual, _f.Servico, RevogadorDe(_f), _f.UnitOfWork,
            Substitute.For<ILogger<EasyStock.Application.UseCases.ContatoUsuario.DefinirMeuTelefoneUseCase>>());

        var acao = () => useCase.ExecuteAsync(
            new EasyStock.Application.UseCases.ContatoUsuario.DefinirMeuTelefoneCommand("+1 415 555 0100", TrocaDeContatoFixture.SenhaCerta));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("TELEFONE_INVALIDO");
        _ana.Telefone.Should().BeNull();
    }

    [Fact]
    public async Task MesmoTelefoneNaoFazNadaNemDerrubaAVerificacao()
    {
        _ana.DefinirTelefone(TelefoneE164.From("11997573992"));
        var verificadoEm = new DateTime(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc);
        _ana.MarcarTelefoneVerificado(verificadoEm);
        var useCase = new EasyStock.Application.UseCases.ContatoUsuario.DefinirMeuTelefoneUseCase(
            _f.Usuarios, _f.UsuarioAtual, _f.Servico, RevogadorDe(_f), _f.UnitOfWork,
            Substitute.For<ILogger<EasyStock.Application.UseCases.ContatoUsuario.DefinirMeuTelefoneUseCase>>());

        var r = await useCase.ExecuteAsync(
            new EasyStock.Application.UseCases.ContatoUsuario.DefinirMeuTelefoneCommand("11997573992", TrocaDeContatoFixture.SenhaCerta));

        r.Verificado.Should().BeTrue();
        _ana.TelefoneVerificadoEm.Should().Be(verificadoEm);
        _f.Eventos.Should().BeEmpty();
        _f.UnitOfWork.CommitCount.Should().Be(0);
    }

    internal static EasyStock.Application.Services.Auth.RevogadorSessoes RevogadorDe(TrocaDeContatoFixture f) =>
        new(f.Usuarios, Substitute.For<IRefreshTokenRepository>(), Substitute.For<ICacheService>(), TimeProvider.System,
            Substitute.For<ILogger<EasyStock.Application.Services.Auth.RevogadorSessoes>>());
}
