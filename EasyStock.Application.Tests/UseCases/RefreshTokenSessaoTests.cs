using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using RefreshTokenEntity = EasyStock.Domain.Entities.RefreshToken;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// M3 (#2070): o refresh renova a sessão pela mesma regra do login (o resolvedor de sessão). O suporte volta SuperAdmin
/// e na mesma empresa da sessão; os demais precisam de vínculo ativo com ela; token antigo sem a coluna segue a regra de
/// antes (a única empresa ativa).
/// </summary>
public class RefreshTokenSessaoTests
{
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly IJwtTokenService _jwt = Substitute.For<IJwtTokenService>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly List<AutenticarUsuarioResult> _emitidos = [];
    private readonly List<RefreshTokenEntity> _novos = [];
    private readonly Guid _empresa = Guid.NewGuid();

    public RefreshTokenSessaoTests()
    {
        _jwt.GerarToken(Arg.Do<AutenticarUsuarioResult>(_emitidos.Add)).Returns("jwt");
        _tokens.AddAsync(Arg.Do<RefreshTokenEntity>(_novos.Add)).Returns(Task.CompletedTask);
    }

    private RefreshTokenUseCase Renovar() => new(
        _tokens, _usuarios, Substitute.For<IAuditLogRepository>(), _jwt, _uow, NullLogger<RefreshTokenUseCase>.Instance);

    private RefreshTokenEntity Sessao(Usuario usuario, Guid? empresaDaSessao)
    {
        var token = RefreshTokenEntity.Criar(usuario.Id, TokenHashHelper.ComputeSha256Hash("velho"), DateTime.UtcNow.AddDays(1),
            null, null, empresaDaSessao);
        _tokens.GetByTokenHashAsync(TokenHashHelper.ComputeSha256Hash("velho")).Returns(token);
        _usuarios.GetByIdAsync(usuario.Id).Returns(usuario);
        return token;
    }

    private static Usuario Suporte()
    {
        var usuario = Usuario.Criar("Suporte", "suporte@exemplo.test", "hash");
        usuario.Perfis.Add(new UsuarioPerfil
        {
            UsuarioId = usuario.Id, EmpresaId = Guid.Empty,
            Perfil = new Perfil { Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin, EmpresaId = null },
        });
        return usuario;
    }

    private static Usuario DaEmpresa(Guid empresa, NivelAcesso nivel, bool ativo = true)
    {
        var usuario = Usuario.Criar("Ana", "ana@exemplo.test", "hash");
        usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = empresa, Ativo = ativo });
        usuario.Perfis.Add(new UsuarioPerfil
        {
            UsuarioId = usuario.Id, EmpresaId = empresa, Perfil = new Perfil { Nome = "P", Nivel = nivel, EmpresaId = empresa },
        });
        return usuario;
    }

    [Fact]
    public async Task Suporte_volta_SuperAdmin_na_mesma_empresa_da_sessao()
    {
        Sessao(Suporte(), _empresa);

        await Renovar().ExecuteAsync(new RefreshTokenCommand("velho"));

        var emitido = _emitidos.Should().ContainSingle().Subject;
        emitido.Nivel.Should().Be(NivelAcesso.SuperAdmin);
        emitido.EmpresaId.Should().Be(_empresa);
        _novos.Should().ContainSingle().Which.EmpresaDaSessaoId.Should().Be(_empresa, "o refresh seguinte renova na mesma empresa");
    }

    [Fact]
    public async Task Token_antigo_do_suporte_volta_SuperAdmin_sem_empresa()
    {
        Sessao(Suporte(), empresaDaSessao: null);

        await Renovar().ExecuteAsync(new RefreshTokenCommand("velho"));

        _emitidos.Single().Nivel.Should().Be(NivelAcesso.SuperAdmin);
        _emitidos.Single().EmpresaId.Should().BeNull();
    }

    [Fact]
    public async Task Quem_tem_duas_empresas_renova_na_empresa_da_sessao()
    {
        var outra = Guid.NewGuid();
        var ana = DaEmpresa(_empresa, NivelAcesso.Operador);
        ana.Empresas.Add(new UsuarioEmpresa { UsuarioId = ana.Id, EmpresaId = outra, Ativo = true });
        ana.Perfis.Add(new UsuarioPerfil { UsuarioId = ana.Id, EmpresaId = outra, Perfil = new Perfil { Nivel = NivelAcesso.Admin, EmpresaId = outra } });
        Sessao(ana, _empresa);

        await Renovar().ExecuteAsync(new RefreshTokenCommand("velho"));

        _emitidos.Single().EmpresaId.Should().Be(_empresa);
        _emitidos.Single().Nivel.Should().Be(NivelAcesso.Operador);
    }

    [Fact]
    public async Task Sem_vinculo_ativo_com_a_empresa_da_sessao_nao_renova()
    {
        var token = Sessao(DaEmpresa(_empresa, NivelAcesso.Admin, ativo: false), _empresa);

        var renovar = () => Renovar().ExecuteAsync(new RefreshTokenCommand("velho"));

        await renovar.Should().ThrowAsync<CredenciaisInvalidasException>();
        _emitidos.Should().BeEmpty();
        _uow.CommitCount.Should().Be(0);
        token.Revogado.Should().BeFalse("nada foi renovado");
    }

    [Fact]
    public async Task Perfil_de_empresa_com_nivel_SuperAdmin_renova_como_Administracao()
    {
        Sessao(DaEmpresa(_empresa, NivelAcesso.SuperAdmin), _empresa);

        await Renovar().ExecuteAsync(new RefreshTokenCommand("velho"));

        _emitidos.Single().Nivel.Should().Be(NivelAcesso.Admin);
        _emitidos.Single().EmpresaId.Should().Be(_empresa);
    }
}
