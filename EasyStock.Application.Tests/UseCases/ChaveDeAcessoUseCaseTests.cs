using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.ChaveDeAcesso;
using EasyStock.Application.UseCases.DesativarUsuario;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #2201: a dona emite uma chave de acesso de uso único; a pessoa a cola uma vez e recebe a sessão dela. Só o hash vai ao
/// banco, a chave vale 30 dias, morre no primeiro uso, e a empresa dentro dela não pode ser trocada.
/// </summary>
public class ChaveDeAcessoUseCaseTests
{
    private const string Ip = "198.51.100.30";
    private readonly CenarioDeAcesso _c = new();
    private readonly ICurrentUserAccessor _atual = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _empresaId = Guid.NewGuid();

    public ChaveDeAcessoUseCaseTests()
    {
        _atual.EmpresaId.Returns(_empresaId);
        _atual.UsuarioId.Returns(Guid.NewGuid());
        _atual.Nivel.Returns(NivelAcesso.Admin);
        _atual.Ip.Returns("198.51.100.9");
    }

    private Usuario CriarDaEmpresa(Guid? empresaId = null)
    {
        var usuario = _c.CriarUsuario(comEmpresa: false);
        usuario.Empresas.Add(new UsuarioEmpresa
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresaId ?? _empresaId, Ativo = true,
        });
        return usuario;
    }

    private EmitirChaveDeAcessoUseCase Emissor() => new(
        _c.Usuarios, _c.Tokens, _atual, _c.Auditoria, _c.UnitOfWork, _c.Relogio,
        Substitute.For<ILogger<EmitirChaveDeAcessoUseCase>>());

    private AtivarComChaveUseCase Ativador() => new(
        _c.Tokens, _c.Usuarios, _c.Auditoria, _c.Limite(), _c.UnitOfWork, _c.Relogio,
        Substitute.For<ILogger<AtivarComChaveUseCase>>());

    private Task<ChaveDeAcessoEmitida> Emitir(Usuario usuario) =>
        Emissor().ExecuteAsync(new EmitirChaveDeAcessoCommand(usuario.Id));

    private Task<AtivarComChaveResult> Ativar(string chave, string? ip = Ip) =>
        Ativador().ExecuteAsync(new AtivarComChaveCommand(chave, ip, "WebView/1.0"));

    [Fact]
    public async Task EmitirGravaSoOHashEValeTrintaDias()
    {
        var usuario = CriarDaEmpresa();

        var emitida = await Emitir(usuario);

        emitida.Chave.Length.Should().BeGreaterThanOrEqualTo(64, "empresa (16 bytes) + 32 bytes aleatórios em base64url");
        var linha = _c.Tokens.Linhas.Should().ContainSingle().Subject;
        linha.Finalidade.Should().Be(FinalidadeResetToken.Ativacao);
        linha.UsuarioId.Should().Be(usuario.Id);
        linha.TokenHash.Should().Be(TokenHashHelper.ComputeSha256Hash(emitida.Chave));
        linha.TokenHash.Should().NotContain(emitida.Chave);
        linha.ExpiraEm.Should().Be(_c.AgoraUtc.AddDays(30));
        emitida.ExpiraEm.Should().Be(linha.ExpiraEm);
        emitida.PrefixoDoHash.Should().Be(linha.TokenHash[..8]);
        _c.UnitOfWork.CommitCount.Should().Be(1);
        _c.Auditorias.Should().ContainSingle(a => a.Acao == ChavesDeAcesso.AcaoEmitida && !a.Detalhes!.Contains(emitida.Chave));
    }

    [Fact]
    public async Task EmitirOutraRevogaAAnterior()
    {
        var usuario = CriarDaEmpresa();
        var primeira = await Emitir(usuario);

        var segunda = await Emitir(usuario);

        _c.Tokens.Linhas.Single(l => l.TokenHash == TokenHashHelper.ComputeSha256Hash(primeira.Chave)).Usado.Should().BeTrue();
        (await Ativar(segunda.Chave)).UsuarioId.Should().Be(usuario.Id);
        await FluentActions.Invoking(() => Ativar(primeira.Chave)).Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task EmitirRecusaUsuarioDeOutraEmpresa()
    {
        var usuario = CriarDaEmpresa(Guid.NewGuid());

        await FluentActions.Invoking(() => Emitir(usuario)).Should().ThrowAsync<UseCaseValidationException>();
        _c.Tokens.Linhas.Should().BeEmpty();
    }

    [Fact]
    public async Task EmitirRecusaSuperAdminComoAlvo()
    {
        var usuario = CriarDaEmpresa();
        CenarioDeAcesso.TornarSuperAdmin(usuario);

        await FluentActions.Invoking(() => Emitir(usuario)).Should().ThrowAsync<UseCaseValidationException>();
        _c.Tokens.Linhas.Should().BeEmpty();
    }

    [Fact]
    public async Task EmitirRecusaUsuarioDesativado()
    {
        var usuario = CriarDaEmpresa();
        usuario.Empresas.Single().Ativo = false;

        await FluentActions.Invoking(() => Emitir(usuario)).Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task SuperAdminEmiteNaEmpresaAtivaDoUsuario()
    {
        _atual.EmpresaId.Returns(Guid.Empty);
        _atual.Nivel.Returns(NivelAcesso.SuperAdmin);
        var outraEmpresa = Guid.NewGuid();
        var usuario = CriarDaEmpresa(outraEmpresa);

        var emitida = await Emitir(usuario);

        (await Ativar(emitida.Chave)).EmpresaId.Should().Be(outraEmpresa);
    }

    [Fact]
    public async Task AtivarDevolveUsuarioEEmpresaEConsome()
    {
        var usuario = CriarDaEmpresa();
        var emitida = await Emitir(usuario);

        var resultado = await Ativar(emitida.Chave);

        resultado.UsuarioId.Should().Be(usuario.Id);
        resultado.EmpresaId.Should().Be(_empresaId);
        _c.Tokens.Linhas.Single().Usado.Should().BeTrue();
        _c.Auditorias.Should().ContainSingle(a => a.Acao == ChavesDeAcesso.AcaoUsada);
    }

    [Fact]
    public async Task UsoUnicoASegundaAtivacaoERecusada()
    {
        var usuario = CriarDaEmpresa();
        var emitida = await Emitir(usuario);
        await Ativar(emitida.Chave);

        await FluentActions.Invoking(() => Ativar(emitida.Chave)).Should()
            .ThrowAsync<RegraDeDominioVioladaException>().WithMessage(AtivarComChaveUseCase.MensagemDeRecusa);
    }

    [Fact]
    public async Task QuemPerdeACorridaDoConsumoNaoEntra()
    {
        var usuario = CriarDaEmpresa();
        var emitida = await Emitir(usuario);
        _c.Tokens.PerderACorridaDoConsumo = true;

        await FluentActions.Invoking(() => Ativar(emitida.Chave)).Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task ChaveVencidaERecusada()
    {
        var usuario = CriarDaEmpresa();
        var emitida = await Emitir(usuario);
        _c.Relogio.Advance(TimeSpan.FromDays(30).Add(TimeSpan.FromSeconds(1)));

        await FluentActions.Invoking(() => Ativar(emitida.Chave)).Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task ChaveRevogadaERecusada()
    {
        var usuario = CriarDaEmpresa();
        var emitida = await Emitir(usuario);

        await Emissor().RevogarAsync(usuario.Id);

        await FluentActions.Invoking(() => Ativar(emitida.Chave)).Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task SegredoDeOutraFinalidadeNaoEntra()
    {
        var usuario = CriarDaEmpresa();
        var chave = ChavesDeAcesso.Gerar(_empresaId);
        await _c.Tokens.AddAsync(ResetToken.Criar(usuario.Id, TokenHashHelper.ComputeSha256Hash(chave),
            _c.AgoraUtc.AddDays(1), null, null, FinalidadeResetToken.Convite));

        await FluentActions.Invoking(() => Ativar(chave)).Should().ThrowAsync<RegraDeDominioVioladaException>();
        _c.Tokens.Linhas.Single().Usado.Should().BeFalse("um convite nunca vira sessão pela chave de acesso");
    }

    [Fact]
    public async Task EmpresaAdulteradaNaChaveERecusada()
    {
        var usuario = CriarDaEmpresa();
        var emitida = await Emitir(usuario);
        var adulterada = ChavesDeAcesso.Gerar(Guid.NewGuid())[..22] + emitida.Chave[22..];

        await FluentActions.Invoking(() => Ativar(adulterada)).Should().ThrowAsync<RegraDeDominioVioladaException>();
        _c.Tokens.Linhas.Single().Usado.Should().BeFalse();
    }

    [Fact]
    public async Task VinculoDesativadoDepoisDaEmissaoNaoEntra()
    {
        var usuario = CriarDaEmpresa();
        var emitida = await Emitir(usuario);
        usuario.Empresas.Single().Ativo = false;

        await FluentActions.Invoking(() => Ativar(emitida.Chave)).Should().ThrowAsync<RegraDeDominioVioladaException>();
        _c.Tokens.Linhas.Single().Usado.Should().BeFalse("a recusa vem antes do consumo");
    }

    [Theory]
    [InlineData("")]
    [InlineData("curta")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public async Task FormatoInvalidoERecusadoComAMesmaMensagem(string chave)
    {
        await FluentActions.Invoking(() => Ativar(chave)).Should()
            .ThrowAsync<RegraDeDominioVioladaException>().WithMessage(AtivarComChaveUseCase.MensagemDeRecusa);
    }

    // Revisão de segurança do #2201: só o superadmin emite chave para um administrador, para um Admin não entrar como
    // outro (a dona, por exemplo) sem a senha dele.
    [Fact]
    public async Task AdminNaoEmiteChaveParaOutroAdmin()
    {
        var usuario = CriarDaEmpresa();
        TornarAdmin(usuario);

        await FluentActions.Invoking(() => Emitir(usuario)).Should().ThrowAsync<UseCaseValidationException>();
        _c.Tokens.Linhas.Should().BeEmpty();
    }

    [Fact]
    public async Task SuperAdminEmiteChaveParaADona()
    {
        _atual.EmpresaId.Returns(Guid.Empty);
        _atual.Nivel.Returns(NivelAcesso.SuperAdmin);
        var dona = CriarDaEmpresa();
        TornarAdmin(dona);

        var emitida = await Emitir(dona);

        (await Ativar(emitida.Chave)).UsuarioId.Should().Be(dona.Id);
    }

    [Fact]
    public async Task DesativarDerrubaAChaveAberta()
    {
        var usuario = CriarDaEmpresa();
        var emitida = await Emitir(usuario);
        var vinculos = Substitute.For<IUsuarioEmpresaRepository>();
        vinculos.GetByUsuarioEEmpresaAsync(usuario.Id, _empresaId).Returns(usuario.Empresas.Single());

        await new DesativarUsuarioUseCase(_c.Usuarios, vinculos, _c.Revogador(), _c.Convites(), _atual, _c.Auditoria,
                _c.UnitOfWork, Substitute.For<ILogger<DesativarUsuarioUseCase>>())
            .ExecuteAsync(new DesativarUsuarioCommand(usuario.Id, _empresaId));
        usuario.Empresas.Single().Ativo = true; // reativada depois

        await FluentActions.Invoking(() => Ativar(emitida.Chave)).Should().ThrowAsync<RegraDeDominioVioladaException>(
            "reativar a pessoa não pode ressuscitar a chave emitida antes da desativação");
    }

    [Fact]
    public async Task ResetDeSenhaDerrubaAChaveAberta()
    {
        var usuario = CriarDaEmpresa();
        var emitida = await Emitir(usuario);

        await _c.Concluidor().ConcluirAsync(usuario, "NovaSenha@12345", "reset-senha", Ip, "WebView/1.0");

        await FluentActions.Invoking(() => Ativar(emitida.Chave)).Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task OutrosPedidosDeAcessoDoMesmoIpNaoTravamAChave()
    {
        var usuario = CriarDaEmpresa();
        var emitida = await Emitir(usuario);
        var limite = _c.Limite();
        for (var i = 0; i < LimitePedidosAcesso.PedidosPorJanela; i++)
            await limite.ExigirAsync(Ip); // forgot-password repetido no Wi-Fi da loja

        (await Ativar(emitida.Chave)).UsuarioId.Should().Be(usuario.Id);
    }

    private static void TornarAdmin(Usuario usuario) =>
        usuario.Perfis.Add(new UsuarioPerfil
        {
            UsuarioId = usuario.Id,
            Perfil = new Perfil { Nome = "Admin", Nivel = NivelAcesso.Admin },
        });

    [Fact]
    public async Task OIpQuePassaDoTetoEBarrado()
    {
        for (var i = 0; i < LimitePedidosAcesso.PedidosPorJanela; i++)
            await FluentActions.Invoking(() => Ativar("errada")).Should().ThrowAsync<RegraDeDominioVioladaException>();

        await FluentActions.Invoking(() => Ativar("errada")).Should().ThrowAsync<LimitePedidosAcessoExcedidoException>();
    }
}
