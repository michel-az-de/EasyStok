using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.UseCases.AtribuirPerfilUsuario;
using EasyStock.Application.UseCases.DesativarUsuario;
using EasyStock.Application.UseCases.ReativarUsuario;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #1753: desativar, reativar e trocar perfil deixam rastro com quem fez; a dona não se tranca para fora; trocar perfil
/// troca de verdade (a homologação de 13/10 achou "Cozinha, Atendimento" no mesmo usuário e a dona desativada por si mesma).
/// </summary>
public class GestaoDeUsuariosUseCaseTests
{
    private readonly CenarioDeAcesso _c = new();
    private readonly ICurrentUserAccessor _atual = Substitute.For<ICurrentUserAccessor>();
    private readonly IUsuarioEmpresaRepository _vinculos = Substitute.For<IUsuarioEmpresaRepository>();
    private readonly IUsuarioPerfilRepository _usuariosPerfis = Substitute.For<IUsuarioPerfilRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IAssinaturaEmpresaRepository _assinaturas = Substitute.For<IAssinaturaEmpresaRepository>();
    private readonly List<UsuarioPerfil> _removidos = [];
    private readonly List<UsuarioPerfil> _adicionados = [];
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Usuario _dona;

    public GestaoDeUsuariosUseCaseTests()
    {
        _dona = Usuario.Criar("Baba", "baba@casadababa.com", "$2a$11$hash");
        _dona.Empresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = _dona.Id, EmpresaId = _empresaId, Ativo = true });
        _c.Usuarios.GetByIdAsync(_dona.Id).Returns(_dona);
        _atual.EmpresaId.Returns(_empresaId);
        _atual.UsuarioId.Returns(_dona.Id);
        _atual.Nivel.Returns(NivelAcesso.Admin);
        _usuariosPerfis.RemoveAsync(Arg.Do<UsuarioPerfil>(_removidos.Add)).Returns(Task.CompletedTask);
        _usuariosPerfis.AddAsync(Arg.Do<UsuarioPerfil>(_adicionados.Add)).Returns(Task.CompletedTask);
    }

    private Usuario Atendente(bool ativo = true, params Perfil[] perfis)
    {
        var usuario = Usuario.Criar("Ana", "ana@casadababa.com", "$2a$11$hash");
        var vinculo = new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = _empresaId, Ativo = ativo };
        usuario.Empresas.Add(vinculo);
        _c.Usuarios.GetByIdAsync(usuario.Id).Returns(usuario);
        _vinculos.GetByUsuarioEEmpresaAsync(usuario.Id, _empresaId).Returns(vinculo);
        var atribuidos = perfis.Select(p => new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = _empresaId, PerfilId = p.Id, Perfil = p,
        }).ToList();
        _usuariosPerfis.ListarPorUsuarioEEmpresaAsync(usuario.Id, _empresaId).Returns(atribuidos);
        return usuario;
    }

    private Perfil NovoPerfil(string nome, NivelAcesso nivel = NivelAcesso.Operador, Guid? empresaId = null)
    {
        var perfil = new Perfil { Id = Guid.NewGuid(), Nome = nome, Nivel = nivel, EmpresaId = empresaId ?? _empresaId };
        _perfis.GetByIdAsync(perfil.Id).Returns(perfil);
        return perfil;
    }

    private DesativarUsuarioUseCase Desativar() => new(
        _c.Usuarios, _vinculos, _c.Revogador(), _c.Convites(), _atual, _c.Auditoria, _c.UnitOfWork, Substitute.For<ILogger<DesativarUsuarioUseCase>>());

    private ReativarUsuarioUseCase Reativar() => new(
        _c.Usuarios, _vinculos, _assinaturas, _atual, _c.Auditoria, _c.UnitOfWork, Substitute.For<ILogger<ReativarUsuarioUseCase>>());

    private AtribuirPerfilUsuarioUseCase Atribuir() => new(
        _c.Usuarios, _usuariosPerfis, _perfis, _c.Revogador(), _atual, _c.Auditoria, _c.UnitOfWork,
        Substitute.For<ILogger<AtribuirPerfilUsuarioUseCase>>());

    [Fact]
    public async Task DesativarDeixaRastroComQuemFez()
    {
        var ana = Atendente();

        await Desativar().ExecuteAsync(new DesativarUsuarioCommand(ana.Id, _empresaId));

        ana.Empresas.Single().Ativo.Should().BeFalse();
        var rastro = _c.Auditorias.Should().ContainSingle().Subject;
        rastro.Acao.Should().Be("usuario-desativado");
        rastro.UsuarioId.Should().Be(ana.Id, "a linha é da pessoa afetada");
        rastro.Detalhes.Should().Contain("Baba", "quem lê precisa do nome de quem fez, não de um identificador");
    }

    [Fact]
    public async Task DonaNaoDesativaASiMesma()
    {
        _vinculos.GetByUsuarioEEmpresaAsync(_dona.Id, _empresaId).Returns(_dona.Empresas.Single());

        var acao = () => Desativar().ExecuteAsync(new DesativarUsuarioCommand(_dona.Id, _empresaId));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("USUARIO_PROPRIO");
        _dona.Empresas.Single().Ativo.Should().BeTrue();
        _c.UnitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task DesativarQuemJaEstaDesativadoNaoRepeteORastro()
    {
        var ana = Atendente(ativo: false);

        await Desativar().ExecuteAsync(new DesativarUsuarioCommand(ana.Id, _empresaId));

        _c.Auditorias.Should().BeEmpty();
    }

    [Fact]
    public async Task ReativarDevolveOAcessoComRastro()
    {
        var ana = Atendente(ativo: false);

        await Reativar().ExecuteAsync(new ReativarUsuarioCommand(ana.Id, _empresaId));

        ana.Empresas.Single().Ativo.Should().BeTrue();
        _c.Auditorias.Should().ContainSingle(a => a.Acao == "usuario-reativado" && a.UsuarioId == ana.Id);
        _c.UnitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task DesativarDerrubaOConviteAberto()
    {
        var ana = _c.CriarConvidado(empresaId: _empresaId);
        _vinculos.GetByUsuarioEEmpresaAsync(ana.Id, _empresaId).Returns(ana.Empresas.Single());
        await _c.Convites().EmitirAsync(ana, _empresaId, comWhatsApp: false, ip: null, userAgent: null, revogarAbertos: false);
        _c.Tokens.Linhas.Should().ContainSingle(t => !t.Usado, "o convite nasce aberto");

        await Desativar().ExecuteAsync(new DesativarUsuarioCommand(ana.Id, _empresaId));

        _c.Tokens.Linhas.Should().OnlyContain(t => t.Usado, "quem foi desativada não pode criar a senha pelo link antigo");
    }

    [Fact]
    public async Task ReativarRespeitaOLimiteDoPlano()
    {
        var ana = Atendente(ativo: false);
        _assinaturas.GetAtivaAsync(_empresaId).Returns(new AssinaturaEmpresa { Plano = new Plano { Nome = "Starter", LimiteUsuarios = 2 } });
        _c.Usuarios.CountByEmpresaAsync(_empresaId).Returns(2);

        var acao = () => Reativar().ExecuteAsync(new ReativarUsuarioCommand(ana.Id, _empresaId));

        await acao.Should().ThrowAsync<PlanoLimiteAtingidoException>();
        ana.Empresas.Single().Ativo.Should().BeFalse();
        _c.Auditorias.Should().BeEmpty();
        _c.UnitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task ReativarUsuarioDeOutraEmpresaNaoAchaNada()
    {
        var alheio = Usuario.Criar("Zé", "ze@outra.com", "$2a$11$hash");
        _c.Usuarios.GetByIdAsync(alheio.Id).Returns(alheio);

        var acao = () => Reativar().ExecuteAsync(new ReativarUsuarioCommand(alheio.Id, _empresaId));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        _c.Auditorias.Should().BeEmpty();
    }

    [Fact]
    public async Task TrocarPerfilTiraOPerfilAnterior()
    {
        var atendimento = NovoPerfil("Atendimento");
        var cozinha = NovoPerfil("Cozinha");
        var ana = Atendente(perfis: atendimento);

        await Atribuir().ExecuteAsync(new AtribuirPerfilUsuarioCommand(ana.Id, _empresaId, cozinha.Id, null));

        _removidos.Should().ContainSingle(p => p.PerfilId == atendimento.Id, "um usuário tem um perfil por empresa");
        var novo = _adicionados.Should().ContainSingle().Subject;
        novo.PerfilId.Should().Be(cozinha.Id);
        novo.AtribuidoPorId.Should().Be(_dona.Id);
        var rastro = _c.Auditorias.Should().ContainSingle(a => a.Acao == "perfil-trocado").Subject;
        rastro.Detalhes.Should().Contain("Atendimento").And.Contain("Cozinha").And.Contain("Baba");
    }

    [Fact]
    public async Task AtribuirOMesmoPerfilNaoGeraRastroNemDerrubaASessao()
    {
        var atendimento = NovoPerfil("Atendimento");
        var ana = Atendente(perfis: atendimento);

        await Atribuir().ExecuteAsync(new AtribuirPerfilUsuarioCommand(ana.Id, _empresaId, atendimento.Id, null));

        _removidos.Should().BeEmpty();
        _adicionados.Should().BeEmpty();
        _c.Auditorias.Should().BeEmpty();
    }

    [Fact]
    public async Task DonaNaoTrocaOProprioPerfil()
    {
        var cozinha = NovoPerfil("Cozinha");

        var acao = () => Atribuir().ExecuteAsync(new AtribuirPerfilUsuarioCommand(_dona.Id, _empresaId, cozinha.Id, null));

        (await acao.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("USUARIO_PROPRIO");
        _adicionados.Should().BeEmpty();
    }
}
