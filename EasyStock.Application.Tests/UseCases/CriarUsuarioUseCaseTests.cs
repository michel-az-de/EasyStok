using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.CriarUsuario;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>N9: a dona cria o usuário sem senha e o sistema manda um convite com link, um token por canal.</summary>
public class CriarUsuarioUseCaseTests
{
    private readonly CenarioDeAcesso _c = new();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _donaId = Guid.NewGuid();
    private readonly IAssinaturaEmpresaRepository _assinaturas = Substitute.For<IAssinaturaEmpresaRepository>();
    private readonly IUsuarioEmpresaRepository _vinculos = Substitute.For<IUsuarioEmpresaRepository>();
    private readonly IUsuarioPerfilRepository _perfisDoUsuario = Substitute.For<IUsuarioPerfilRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ICurrentUserAccessor _atual = Substitute.For<ICurrentUserAccessor>();
    private readonly List<Usuario> _adicionados = [];

    public CriarUsuarioUseCaseTests()
    {
        _atual.UsuarioId.Returns(_donaId);
        _atual.EmpresaId.Returns(_empresaId);
        _atual.Ip.Returns("198.51.100.9");
        _c.Usuarios.AddAsync(Arg.Do<Usuario>(_adicionados.Add)).Returns(Task.CompletedTask);
        _hasher.Hash(Arg.Any<string>()).Returns("$2a$11$hash-da-senha");
    }

    private CriarUsuarioUseCase UseCase() => new(
        _c.Usuarios, _assinaturas, _vinculos, _perfisDoUsuario, _perfis, _c.Consentimentos, _c.Convites(), _atual,
        _c.UnitOfWork, _hasher, Substitute.For<ILogger<CriarUsuarioUseCase>>());

    private CriarUsuarioCommand Convite(
        string? telefone = null, bool atesta = false, Guid? perfilId = null, string? senha = null) =>
        new(_empresaId, "Ana Souza", "ana@casadababa.com", senha, perfilId, null, telefone, atesta);

    [Fact]
    public async Task SemSenhaCriaConvidadoEEmiteConviteDeEmail()
    {
        var resultado = await UseCase().ExecuteAsync(Convite());

        var usuario = _adicionados.Should().ContainSingle().Subject;
        usuario.Ativo.Should().BeTrue();
        usuario.ConvitePendente.Should().BeTrue();
        usuario.EmailConfirmado.Should().BeFalse();
        resultado.UsuarioId.Should().Be(usuario.Id);
        await _vinculos.Received(1).AddAsync(Arg.Is<UsuarioEmpresa>(v => v.UsuarioId == usuario.Id && v.EmpresaId == _empresaId && v.Ativo));
        _hasher.DidNotReceive().Hash(Arg.Any<string>());

        var evento = _c.Eventos.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be(TipoEventoNotificacao.ConviteAcesso);
        evento.EmpresaId.Should().Be(_empresaId);
        evento.Payload.GetProperty("usuarioId").GetGuid().Should().Be(usuario.Id);
        evento.Payload.GetProperty("email").GetString().Should().Be("ana@casadababa.com");
        evento.Payload.GetProperty("empresa").GetString().Should().Be("Casa da Baba");
        evento.Payload.GetProperty("link_convite").GetString().Should().StartWith("https://app.easystok.com.br/auth/convite?token=");
        evento.Payload.GetProperty("expira_em_dias").GetInt32().Should().Be(3);
        _c.UnitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task OTokenDoEmailTem32BytesEFicaSoOHashNoBanco()
    {
        await UseCase().ExecuteAsync(Convite());

        var link = _c.Eventos[0].Payload.GetProperty("link_convite").GetString()!;
        var token = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);
        Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=").Should().HaveCount(32);

        var linha = _c.Tokens.Linhas.Should().ContainSingle().Subject;
        linha.Finalidade.Should().Be(FinalidadeResetToken.Convite);
        linha.Canal.Should().Be("Email");
        linha.TokenHash.Should().Be(SegredosDeAcesso.HashDoLink(token)).And.NotContain(token);
    }

    [Fact]
    public async Task ComTelefoneEAtestadoLevaOTokenDeWhatsAppNoPayload()
    {
        await UseCase().ExecuteAsync(Convite(telefone: "(11) 99999-1234", atesta: true));

        var usuario = _adicionados.Single();
        usuario.Telefone!.Value.Should().Be("+5511999991234");
        usuario.TelefoneVerificadoEm.Should().BeNull("quem verifica o telefone é o aceite do convite");

        _c.Eventos.Should().ContainSingle();
        var payload = _c.Eventos[0].Payload;
        var tokenWhats = payload.GetProperty("token_convite_whatsapp").GetString()!;
        payload.TryGetProperty("canais", out _).Should().BeFalse("sem a chave de controle o modo todos usa os dois canais");

        _c.Tokens.Linhas.Select(l => l.Canal).Should().BeEquivalentTo("Email", "WhatsApp");
        var linhaWhats = _c.Tokens.Linhas.Single(l => l.Canal == "WhatsApp");
        linhaWhats.TokenHash.Should().Be(SegredosDeAcesso.HashDoLink(tokenWhats));
        linhaWhats.Finalidade.Should().Be(FinalidadeResetToken.Convite);

        var linkEmail = payload.GetProperty("link_convite").GetString()!;
        linkEmail.Should().NotContain(tokenWhats, "cada canal tem o seu token");

        var consentimento = _c.ConsentimentosGravados.Should().ContainSingle().Subject;
        consentimento.Canal.Should().Be(CanalNotificacao.WhatsApp);
        consentimento.Categoria.Should().Be(CategoriaConteudoNotificacao.Seguranca);
        consentimento.OptIn.Should().BeTrue();
        consentimento.UsuarioId.Should().Be(usuario.Id);
        consentimento.AtualizadoPor.Should().Be($"usuario:{_donaId}", "a dona é a autora do atestado");
        consentimento.IpOrigem.Should().Be("198.51.100.9");
    }

    [Fact]
    public async Task SemAtestadoRestringeOsCanaisAoEmailESemTokenDeWhatsApp()
    {
        await UseCase().ExecuteAsync(Convite(telefone: "(11) 99999-1234", atesta: false));

        var payload = _c.Eventos.Should().ContainSingle().Subject.Payload;
        payload.GetProperty("canais").EnumerateArray().Select(e => e.GetString()).Should().Equal("Email");
        payload.TryGetProperty("token_convite_whatsapp", out _).Should().BeFalse();
        _c.Tokens.Linhas.Should().ContainSingle().Which.Canal.Should().Be("Email");
        _c.ConsentimentosGravados.Should().BeEmpty();
    }

    [Fact]
    public async Task AtestadoSemTelefoneNaoCriaTokenDeWhatsApp()
    {
        await UseCase().ExecuteAsync(Convite(telefone: null, atesta: true));

        _c.Eventos.Single().Payload.GetProperty("canais").EnumerateArray().Select(e => e.GetString()).Should().Equal("Email");
        _c.Tokens.Linhas.Should().ContainSingle();
        _c.ConsentimentosGravados.Should().BeEmpty();
    }

    [Fact]
    public async Task ConviteExpiraEmSetentaEDuasHoras()
    {
        await UseCase().ExecuteAsync(Convite(telefone: "(11) 99999-1234", atesta: true));

        _c.Tokens.Linhas.Should().HaveCount(2)
            .And.OnlyContain(l => l.ExpiraEm == _c.AgoraUtc.AddHours(72));
    }

    [Fact]
    public async Task ConvidarSuperAdminRecusa()
    {
        var perfilGlobal = new Perfil { Id = Guid.NewGuid(), Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin };
        _perfis.GetByIdAsync(perfilGlobal.Id).Returns(perfilGlobal);

        var act = () => UseCase().ExecuteAsync(Convite(perfilId: perfilGlobal.Id));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*uperadmin*");
        _adicionados.Should().BeEmpty();
        _c.Eventos.Should().BeEmpty();
        _c.UnitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task ConviteSemBaseDeLinkRecusaAntesDeCriar()
    {
        _c.Configuracao["Auth:LinkConvite"] = "";

        var act = () => UseCase().ExecuteAsync(Convite());

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _adicionados.Should().BeEmpty();
    }

    [Fact]
    public async Task ComSenhaSegueComoHoje()
    {
        var resultado = await UseCase().ExecuteAsync(Convite(senha: "Senha@12345"));

        var usuario = _adicionados.Should().ContainSingle().Subject;
        usuario.SenhaHash.Should().Be("$2a$11$hash-da-senha");
        usuario.ConvitePendente.Should().BeFalse();
        resultado.UsuarioId.Should().Be(usuario.Id);
        _hasher.Received(1).Hash("Senha@12345");
        _c.Eventos.Should().BeEmpty();
        _c.Tokens.Linhas.Should().BeEmpty();
    }

    [Fact]
    public async Task EmailJaCadastradoSegueRecusando()
    {
        _c.CriarUsuario("ana@casadababa.com");

        var act = () => UseCase().ExecuteAsync(Convite());

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("Email ja cadastrado.");
    }

    [Fact]
    public async Task TelefoneInvalidoRecusaAntesDeCriar()
    {
        var act = () => UseCase().ExecuteAsync(Convite(telefone: "123"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _adicionados.Should().BeEmpty();
    }
}
