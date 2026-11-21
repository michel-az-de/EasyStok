using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases;

public sealed class SessoesNativasTests
{
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IAuditLogRepository _audit = Substitute.For<IAuditLogRepository>();
    private readonly IJwtTokenService _jwt = Substitute.For<IJwtTokenService>();
    private readonly EasyStock.Application.Ports.Output.Auth.IProtetorRefreshNativo _protetor = Substitute.For<EasyStock.Application.Ports.Output.Auth.IProtetorRefreshNativo>();
    private readonly Dictionary<(Guid, string), string> _recibos = [];
    private readonly FakeUnitOfWork _uow = new();
    private readonly Usuario _usuario = Usuario.Criar("Ana", "ana@prova.test", "hash");
    private readonly SessaoNativa _sessao;
    private readonly List<Domain.Entities.RefreshToken> _emitidos = [];
    private readonly HashSet<Guid> _consumidos = [];
    private AutenticarUsuarioResult? _claims;
    private const string Segredo = "refresh-exclusivo-da-prova";

    public SessoesNativasTests()
    {
        _sessao = new SessaoNativa { UsuarioId = _usuario.Id, EmpresaId = Guid.NewGuid(), DispositivoId = "android-2184", CriadaEm = DateTime.UtcNow.AddMinutes(-5) };
        _usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = _usuario.Id, EmpresaId = _sessao.EmpresaId, Ativo = true });
        _usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = _usuario.Id, EmpresaId = Guid.NewGuid(), Ativo = true });
        _usuario.Perfis.Add(new UsuarioPerfil { UsuarioId = _usuario.Id, EmpresaId = _sessao.EmpresaId,
            Perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = _sessao.EmpresaId, Nome = "Gerente", Nivel = NivelAcesso.Gerente, PermissoesExplicitas = true } });
        var refresh = Domain.Entities.RefreshToken.Criar(_usuario.Id, TokenHashHelper.ComputeSha256Hash(Segredo), DateTime.MaxValue, null, null);
        refresh.SessaoNativaId = _sessao.Id;
        _emitidos.Add(refresh);
        _tokens.GetByTokenHashAsync(Arg.Any<string>()).Returns(c => _emitidos.SingleOrDefault(t => t.TokenHash == c.Arg<string>()));
        _tokens.ObterSessaoNativaComLockAsync(_sessao.Id).Returns(_ =>
        {
            Assert.True(_uow.EmTransacao, "a trava da família precisa pertencer à transação inteira");
            return _sessao;
        });
        _tokens.ConsumirAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(c => _consumidos.Add(c.Arg<Guid>()));
        _protetor.Proteger(Arg.Any<Guid>(), Arg.Any<string>()).Returns(c =>
        {
            var recibo = "recibo-sintetico-" + Guid.NewGuid();
            _recibos[(c.Arg<Guid>(), recibo)] = c.Arg<string>();
            return recibo;
        });
        _protetor.Desproteger(Arg.Any<Guid>(), Arg.Any<string>()).Returns(c => _recibos[(c.Arg<Guid>(), c.Arg<string>())]);
        _tokens.SalvarReciboRenovacaoAsync(_sessao.Id, Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>()).Returns(c =>
        {
            Assert.True(_uow.EmTransacao);
            _sessao.UltimaRenovacaoId = c.ArgAt<Guid?>(1);
            _sessao.RefreshAnteriorId = c.ArgAt<Guid?>(2);
            _sessao.RefreshSucessorProtegido = c.ArgAt<string?>(3);
            return Task.CompletedTask;
        });
        _tokens.AddAsync(Arg.Any<Domain.Entities.RefreshToken>()).Returns(c => { _emitidos.Add(c.Arg<Domain.Entities.RefreshToken>()); return Task.CompletedTask; });
        _tokens.RevogarSessaoNativaAsync(_sessao.Id, Arg.Any<DateTime>()).Returns(c => { _sessao.RevogadaEm = c.Arg<DateTime>(); return Task.CompletedTask; });
        _usuarios.GetByIdAsync(_usuario.Id).Returns(_usuario);
        _jwt.GerarToken(Arg.Any<AutenticarUsuarioResult>()).Returns(c => { _claims = c.Arg<AutenticarUsuarioResult>(); return "jwt-da-prova"; });
        _jwt.ExpiresInSeconds.Returns(3600);
    }

    private RefreshTokenUseCase Renovacao() => new(_tokens, _usuarios, _audit, _jwt, _uow, NullLogger<RefreshTokenUseCase>.Instance, _protetor);
    private LogoutUseCase Saida() => new(_tokens, _usuarios,
        new RevogadorSessoes(_usuarios, _tokens, Substitute.For<ICacheService>(), TimeProvider.System, NullLogger<RevogadorSessoes>.Instance),
        _audit, _uow, NullLogger<LogoutUseCase>.Instance);

    [Fact]
    public async Task RenovacaoMantemEmpresaAparelhoEFamilia_AtualizandoPermissoes()
    {
        var resultado = await Renovacao().ExecuteAsync(new(Segredo));
        Assert.Equal(_sessao.EmpresaId, _claims!.EmpresaId);
        Assert.Equal(_sessao.Id, _claims.SessaoId);
        Assert.Equal(_sessao.DispositivoId, _claims.DispositivoId);
        Assert.Equal(NivelAcesso.Gerente, _claims.Nivel);
        Assert.True(_claims.PermissoesExplicitas);
        Assert.True(_claims.SessaoPersistente);
        Assert.Equal(3600, resultado.ExpiresIn);
        Assert.NotEqual(Segredo, resultado.RefreshToken);
        Assert.Equal(_sessao.Id, _emitidos.Last().SessaoNativaId);
        Assert.Equal(DateTime.MaxValue, _emitidos.Last().ExpiraEm);
        _usuario.Perfis.Single().Perfil!.Nivel = NivelAcesso.Visualizador;
        await Renovacao().ExecuteAsync(new(resultado.RefreshToken));
        Assert.Equal(NivelAcesso.Visualizador, _claims.Nivel);
    }

    [Fact]
    public async Task PerfilGlobalSuperAdminPermaneceNaRenovacaoNativa()
    {
        _usuario.Perfis.Clear();
        _usuario.Perfis.Add(new UsuarioPerfil { UsuarioId = _usuario.Id, EmpresaId = Guid.Empty,
            Perfil = new Perfil { Id = Guid.NewGuid(), Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin } });
        await Renovacao().ExecuteAsync(new(Segredo));
        Assert.Equal(NivelAcesso.SuperAdmin, _claims!.Nivel);
        Assert.Equal(_sessao.EmpresaId, _claims.EmpresaId);
    }

    [Fact]
    public async Task RespostaPerdidaRepeteMesmoSucessor_ComNoncePersistido_MasSairPrevalece()
    {
        var command = System.Text.Json.JsonSerializer.Deserialize<RefreshTokenCommand>(
            System.Text.Json.JsonSerializer.Serialize(new { refreshToken = Segredo, requisicaoId = Guid.NewGuid() }),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var perdida = await Renovacao().ExecuteAsync(command);
        // O cliente ainda só tem o segredo/nonce antigos quando a resposta some depois do commit.
        var repetida = await Renovacao().ExecuteAsync(command);
        Assert.Equal(perdida.RefreshToken, repetida.RefreshToken);
        Assert.Equal(2, _emitidos.Count);
        await Saida().ExecuteAsync(new(Segredo));
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao().ExecuteAsync(command));
    }

    [Fact]
    public async Task ReciboNaoAceitaOutroNonceNemMesmoNonceComOutroToken_EAtualizaPermissoes()
    {
        var command = new RefreshTokenCommand(Segredo, Guid.NewGuid());
        var primeira = await Renovacao().ExecuteAsync(command);
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao().ExecuteAsync(new(Segredo, Guid.NewGuid())));
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao().ExecuteAsync(new(primeira.RefreshToken, command.RequisicaoId)));
        _usuario.Perfis.Single().Perfil!.Nivel = NivelAcesso.Visualizador;
        Assert.Equal(primeira.RefreshToken, (await Renovacao().ExecuteAsync(command)).RefreshToken);
        Assert.Equal(NivelAcesso.Visualizador, _claims!.Nivel);
        Assert.Equal(2, _emitidos.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProximaRotacaoSubstituiReciboInclusiveSemNonce(bool comNonce)
    {
        var command = new RefreshTokenCommand(Segredo, Guid.NewGuid());
        var primeira = await Renovacao().ExecuteAsync(command);
        var proxima = await Renovacao().ExecuteAsync(new(primeira.RefreshToken, comNonce ? Guid.NewGuid() : null));
        Assert.NotEqual(primeira.RefreshToken, proxima.RefreshToken);
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao().ExecuteAsync(command));
        if (!comNonce) Assert.Null(_sessao.RefreshSucessorProtegido);
    }

    [Theory]
    [InlineData("usuario")]
    [InlineData("empresa")]
    [InlineData("corte")]
    [InlineData("aparelho")]
    public async Task ReciboRevalidaAcessoAtual(string alteracao)
    {
        var command = new RefreshTokenCommand(Segredo, Guid.NewGuid());
        await Renovacao().ExecuteAsync(command);
        if (alteracao == "usuario") _usuario.Ativo = false;
        if (alteracao == "empresa") _usuario.Empresas.Single(e => e.EmpresaId == _sessao.EmpresaId).Ativo = false;
        if (alteracao == "corte") _usuario.SessoesValidasDesde = DateTime.UtcNow;
        if (alteracao == "aparelho") _tokens.DispositivoRevogadoAsync(_sessao.DispositivoId).Returns(true);
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao().ExecuteAsync(command));
    }

    [Fact]
    public async Task NonceVazioNaoConsomeRefresh()
    {
        await Assert.ThrowsAsync<EasyStock.Application.UseCases.Common.UseCaseValidationException>(() => Renovacao().ExecuteAsync(new(Segredo, Guid.Empty)));
        Assert.Single(_emitidos);
        await _tokens.DidNotReceive().ConsumirAsync(Arg.Any<Guid>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task MesmoRefreshNaoGeraDoisSucessores()
    {
        await Renovacao().ExecuteAsync(new(Segredo));
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao().ExecuteAsync(new(Segredo)));
        Assert.Equal(2, _emitidos.Count);
    }

    [Fact]
    public async Task SairComRefreshJaRotacionadoRevogaSucessor_SemRevogarPc()
    {
        var renovado = await Renovacao().ExecuteAsync(new(Segredo));
        Assert.True((await Saida().ExecuteAsync(new(Segredo))).Success);
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao().ExecuteAsync(new(renovado.RefreshToken)));
        await _tokens.DidNotReceive().RevogarSessoesAtivasAsync(Arg.Any<Guid>(), Arg.Any<DateTime>());
        await _usuarios.DidNotReceive().AtualizarSessoesValidasDesdeAsync(Arg.Any<Guid>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task RevogacaoAdministrativaNaoRessuscitaComIatNovo()
    {
        _usuario.SessoesValidasDesde = _sessao.CriadaEm.AddSeconds(1);
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao().ExecuteAsync(new(Segredo)));
        Assert.Single(_emitidos);
    }

    [Fact]
    public async Task VinculoRemovidoOuAparelhoRevogadoImpedeRenovar()
    {
        _usuario.Empresas.Single(e => e.EmpresaId == _sessao.EmpresaId).Ativo = false;
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao().ExecuteAsync(new(Segredo)));
        _usuario.Empresas.Single(e => e.EmpresaId == _sessao.EmpresaId).Ativo = true;
        _tokens.DispositivoRevogadoAsync(_sessao.DispositivoId).Returns(true);
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao().ExecuteAsync(new(Segredo)));
        Assert.Single(_emitidos);
    }
}
