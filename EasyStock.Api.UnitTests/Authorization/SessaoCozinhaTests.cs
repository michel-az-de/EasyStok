using System.IdentityModel.Tokens.Jwt;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Authorization;

public class SessaoCozinhaTests
{
    [Theory]
    [InlineData("Cozinha", "normal", true)]
    [InlineData("Dona", "normal", false)]
    [InlineData("Atendimento", "normal", false)]
    [InlineData("Cozinha", "outra-empresa", false)]
    [InlineData("Cozinha", "global", false)]
    [InlineData("Cozinha", "gerente", false)]
    [InlineData("Cozinha", "permissao-extra", false)]
    [InlineData("Cozinha", "sem-permissoes", false)]
    [InlineData("Cozinha", "varias-empresas", false)]
    public async Task SenhaGoogleERefresh_SoPersistemCozinhaRestritaDaEmpresa(string nome, string caso, bool esperado)
    {
        var empresa = Guid.NewGuid();
        var modelo = PerfisCasaDaBaba.Iniciais.Single(p => p.Nome == nome);
        var perfil = new Perfil { Id = Guid.NewGuid(), Nome = nome, EmpresaId = empresa,
            Nivel = modelo.Nivel, ModuloInicial = modelo.ModuloInicial, PermissoesExplicitas = true,
            Permissoes = modelo.Permissoes.Select(p => new PerfilPermissao { Permissao = p }).ToList() };
        if (caso == "outra-empresa") perfil.EmpresaId = Guid.NewGuid();
        if (caso == "global") perfil.EmpresaId = null;
        if (caso == "gerente") perfil.Nivel = NivelAcesso.Gerente;
        if (caso == "permissao-extra") perfil.Permissoes.Add(new PerfilPermissao { Permissao = Permissao.AcessarModuloCaixa });
        if (caso == "sem-permissoes") perfil.Permissoes.Clear();
        var usuario = Usuario.Criar("Pessoa de teste", "pessoa@teste.local", "hash-fixture");
        usuario.Empresas.Add(new UsuarioEmpresa { EmpresaId = empresa, Ativo = true });
        usuario.Perfis.Add(new UsuarioPerfil { EmpresaId = empresa, Perfil = perfil, PerfilId = perfil.Id });
        if (caso == "varias-empresas") usuario.Empresas.Add(new UsuarioEmpresa { EmpresaId = Guid.NewGuid(), Ativo = true });
        var usuarios = Substitute.For<IUsuarioRepository>();
        usuarios.GetByEmailAsync(usuario.Email).Returns(usuario);
        usuarios.GetByIdAsync(usuario.Id).Returns(usuario);
        var uow = Substitute.For<IUnitOfWork>();
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Verify("senha-fixture", usuario.SenhaHash).Returns(true);
        var login = new AutenticarUsuarioUseCase(usuarios, uow, hasher, NullLogger<AutenticarUsuarioUseCase>.Instance);
        var jwt = new JwtTokenService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Jwt:SecretKey"] = "chave-fixture-sessao-com-mais-de-32-caracteres" }).Build());
        foreach (var resultado in new[] {
            await login.ExecuteAsync(new AutenticarUsuarioCommand(usuario.Email, "senha-fixture", empresa)),
            await login.ConcluirLoginGoogleAsync(usuario, empresa) })
        {
            resultado.SessaoPersistente.Should().Be(esperado);
            Persistente(jwt.GerarToken(resultado)).Should().Be(esperado);
        }

        var tokens = new Dictionary<string, RefreshToken>();
        tokens[TokenHashHelper.ComputeSha256Hash("inicial")] = RefreshToken.Criar(usuario.Id,
            TokenHashHelper.ComputeSha256Hash("inicial"), DateTime.UtcNow.AddDays(1), null, null);
        var repositorio = Substitute.For<IRefreshTokenRepository>();
        repositorio.GetByTokenHashAsync(Arg.Any<string>()).Returns(c => tokens.GetValueOrDefault(c.Arg<string>()));
        repositorio.AddAsync(Arg.Any<RefreshToken>()).Returns(c => {
            var token = c.Arg<RefreshToken>(); tokens[token.TokenHash] = token; return Task.CompletedTask;
        });
        var audit = Substitute.For<IAuditLogRepository>();
        var renovar = new RefreshTokenUseCase(repositorio, usuarios, audit, jwt, uow, NullLogger<RefreshTokenUseCase>.Instance);
        var renovada = await renovar.ExecuteAsync(new RefreshTokenCommand("inicial"));
        Persistente(renovada.AccessToken).Should().Be(esperado);
        tokens[TokenHashHelper.ComputeSha256Hash("inicial")].Revogado.Should().BeTrue();

        // A capacidade é recalculada, não copiada da sessão antiga.
        perfil.Permissoes.Add(new PerfilPermissao { Permissao = Permissao.AcessarModuloCaixa });
        var alterada = await renovar.ExecuteAsync(new RefreshTokenCommand(renovada.RefreshToken));
        Persistente(alterada.AccessToken).Should().BeFalse();
        await new LogoutUseCase(repositorio, audit, uow, NullLogger<LogoutUseCase>.Instance)
            .ExecuteAsync(new LogoutCommand(alterada.RefreshToken));
        var tentar = () => renovar.ExecuteAsync(new RefreshTokenCommand(alterada.RefreshToken));
        await tentar.Should().ThrowAsync<CredenciaisInvalidasException>();
    }

    private static bool Persistente(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token)
        .Claims.Any(c => c.Type == "sessaoPersistente" && c.Value == "true");
}
