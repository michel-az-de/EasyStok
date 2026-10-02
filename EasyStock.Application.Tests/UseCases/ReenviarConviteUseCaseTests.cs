using EasyStock.Application.Ports.Output;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.ReenviarConvite;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>N9: reenviar revoga os convites abertos e emite novos, no máximo 3 por hora por usuário (contados nas linhas).</summary>
public class ReenviarConviteUseCaseTests
{
    private readonly CenarioDeAcesso _c = new();
    private readonly ICurrentUserAccessor _atual = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _empresaId = Guid.NewGuid();

    public ReenviarConviteUseCaseTests()
    {
        _atual.EmpresaId.Returns(_empresaId);
        _atual.UsuarioId.Returns(Guid.NewGuid());
        _atual.Nivel.Returns(NivelAcesso.Admin);
        _atual.Ip.Returns("198.51.100.9");
    }

    private ReenviarConviteUseCase UseCase() => new(
        _c.Usuarios, _atual, _c.Convites(), _c.Tenant, _c.Auditoria, _c.UnitOfWork, Substitute.For<ILogger<ReenviarConviteUseCase>>());

    private Task Reenviar(Usuario usuario) => UseCase().ExecuteAsync(new ReenviarConviteCommand(usuario.Id));

    [Fact]
    public async Task RevogaOsAbertosEEmiteNovos()
    {
        var usuario = _c.CriarConvidado(empresaId: _empresaId);
        await _c.Convites().EmitirAsync(usuario, _empresaId, false, null, null);
        var antigo = _c.Tokens.Linhas.Single();
        _c.Eventos.Clear();

        await Reenviar(usuario);

        antigo.Usado.Should().BeTrue("o convite anterior morre no reenvio");
        var novo = _c.Tokens.Linhas.Single(l => !l.Usado);
        novo.Finalidade.Should().Be(FinalidadeResetToken.Convite);
        novo.ExpiraEm.Should().Be(_c.AgoraUtc.AddHours(72));
        var evento = _c.Eventos.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be(TipoEventoNotificacao.ConviteAcesso);
        evento.EmpresaId.Should().Be(_empresaId);
        _c.UnitOfWork.CommitCount.Should().Be(1);
        _c.Auditorias.Should().ContainSingle(a => a.Acao == "convite-reenviado");
    }

    [Fact]
    public async Task ComAtestadoAnteriorReemiteTambemPeloWhatsApp()
    {
        var usuario = _c.CriarConvidado(empresaId: _empresaId, telefone: "+5511999991234");
        _c.Consentimentos.ListarPorUsuariosAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([ConsentimentoNotificacao.Registrar(
                usuario.Id, CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Seguranca, true, "usuario:dona")]);

        await Reenviar(usuario);

        _c.Tokens.Linhas.Select(l => l.Canal).Should().BeEquivalentTo("Email", "WhatsApp");
        _c.Eventos.Single().Payload.TryGetProperty("token_convite_whatsapp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SemAtestadoReemiteSoPorEmail()
    {
        var usuario = _c.CriarConvidado(empresaId: _empresaId, telefone: "+5511999991234");

        await Reenviar(usuario);

        _c.Tokens.Linhas.Should().ContainSingle().Which.Canal.Should().Be("Email");
        _c.Eventos.Single().Payload.GetProperty("canais").EnumerateArray().Select(e => e.GetString()).Should().Equal("Email");
    }

    [Fact]
    public async Task TerceiroDaHoraPassaEQuartoNao()
    {
        var usuario = _c.CriarConvidado(empresaId: _empresaId);

        await Reenviar(usuario);
        await Reenviar(usuario);
        await Reenviar(usuario);
        var quarto = () => Reenviar(usuario);

        await quarto.Should().ThrowAsync<LimitePedidosAcessoExcedidoException>();
        _c.Eventos.Should().HaveCount(3);
        _c.Tokens.Linhas.Count(l => !l.Usado).Should().Be(1, "só o terceiro convite segue aberto");
    }

    [Fact]
    public async Task UmaHoraDepoisOLimiteLibera()
    {
        var usuario = _c.CriarConvidado(empresaId: _empresaId);
        for (var i = 0; i < 3; i++) await Reenviar(usuario);
        _c.Relogio.Advance(TimeSpan.FromMinutes(61));

        await Reenviar(usuario);

        _c.Eventos.Should().HaveCount(4);
    }

    [Fact]
    public async Task AdminDeOutraEmpresaNaoReenvia()
    {
        var usuario = _c.CriarConvidado(empresaId: Guid.NewGuid());

        var act = () => Reenviar(usuario);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("Usuario nao encontrado.");
        _c.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task QuemJaAceitouNaoRecebeConvite()
    {
        var usuario = _c.CriarConvidado(empresaId: _empresaId);
        usuario.AceitarConvite("hash:Senha@12345", ViaDoConvite.Email, _c.AgoraUtc);

        var act = () => Reenviar(usuario);

        await act.Should().ThrowAsync<UseCaseValidationException>().Where(e => e.Code == "CONVITE_NAO_PENDENTE");
        _c.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task SuperAdminNuncaRecebeConvite()
    {
        var usuario = _c.CriarConvidado(empresaId: _empresaId);
        CenarioDeAcesso.TornarSuperAdmin(usuario);

        var act = () => Reenviar(usuario);

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _c.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task SuperAdminDaPlataformaReenviaNaEmpresaDoUsuario()
    {
        _atual.Nivel.Returns(NivelAcesso.SuperAdmin);
        _atual.EmpresaId.Returns(Guid.Empty);
        var empresaDoUsuario = Guid.NewGuid();
        var usuario = _c.CriarConvidado(empresaId: empresaDoUsuario);

        await Reenviar(usuario);

        _c.Eventos.Single().EmpresaId.Should().Be(empresaDoUsuario);
        _c.Tenant.Received(1).SetCurrentTenant(empresaDoUsuario);
    }
}
