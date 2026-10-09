using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using EasyStock.Api.Data;
using EasyStock.Api.Services;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Services;
using EasyStock.Infra.Async;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Migrations;

public class PermissoesLegadasMigrationTests
{
    [Fact]
    public async Task ArquivamentoDaCasaPreservaLoginRefreshEFallbackSemAlterarOutrasEmpresas()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        var hasher = new BCryptPasswordHasher();
        const string senha = "Teste-Local-Permissoes-123!";
        var hash = hasher.Hash(senha);
        var empresas = new[] { Empresa.Criar("Casa teste", null), Empresa.Criar("Outra teste", null) };
        var casos = new[] { "legado", "misto", "fallback", "outra-empresa", "global" };
        var usuarios = casos.Select(nome => Usuario.Criar(nome, $"{nome}@teste.local", hash)).ToArray();
        var perfilIds = casos.Select(_ => Guid.NewGuid()).ToArray();
        var idsLegadas = new Dictionary<Guid, List<Guid>>();

        await using (var db = new EasyStockDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261009170822_AddPerfilModuloInicial");
            db.Empresas.AddRange(empresas);
            db.Usuarios.AddRange(usuarios);
            await db.SaveChangesAsync();
            for (var i = 0; i < casos.Length; i++)
            {
                var empresaId = empresas[i == 3 ? 1 : 0].Id;
                Guid? empresaPerfil = i == 4 ? null : empresaId;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO perfis ("Id", "EmpresaId", "Nome", "Nivel", "CriadoEm")
                    VALUES ({perfilIds[i]}, {empresaPerfil}, {casos[i]}, 'Admin', {DateTime.UtcNow})
                    """);
                idsLegadas[perfilIds[i]] = [];
                foreach (var permissao in i == 2 ? [] : PermissoesLegadas.Valores.ToArray())
                {
                    var id = Guid.NewGuid();
                    idsLegadas[perfilIds[i]].Add(id);
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO perfis_permissoes ("Id", "PerfilId", "Permissao")
                        VALUES ({id}, {perfilIds[i]}, {permissao.ToString()})
                        """);
                }
                if (i == 1)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO perfis_permissoes ("Id", "PerfilId", "Permissao")
                        VALUES ({Guid.NewGuid()}, {perfilIds[i]}, 'AtenderConversas')
                        """);
                db.UsuariosEmpresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = usuarios[i].Id,
                    EmpresaId = empresaId, Ativo = true, CriadoEm = DateTime.UtcNow });
                db.UsuariosPerfis.Add(new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = usuarios[i].Id,
                    EmpresaId = empresaId, PerfilId = perfilIds[i], AtribuidoEm = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
            await db.Database.MigrateAsync();
            // A migration só adiciona colunas: nenhum dado de nenhuma empresa é limpo por ela.
            (await db.Set<PerfilPermissao>().CountAsync()).Should().Be(73);
            await PerfisCasaDaBabaSeed.ExecutarAsync(db, empresas[0].Id);
            await PerfisCasaDaBabaSeed.ExecutarAsync(db, empresas[0].Id);
            await db.Database.MigrateAsync();
            var perfis = await db.Perfis.IgnoreQueryFilters().Include(p => p.Permissoes).ToListAsync();
            perfis.Should().HaveCount(8);
            for (var i = 0; i < casos.Length; i++)
            {
                var perfil = perfis.Single(p => p.Id == perfilIds[i]);
                perfil.PermissoesExplicitas.Should().Be(i < 2);
                var arquivo = db.Entry(perfil).Property<string?>("PermissoesLegadas").CurrentValue;
                if (i < 2)
                {
                    using var json = JsonDocument.Parse(arquivo!);
                    var registros = json.RootElement.EnumerateArray().ToArray();
                    registros.Select(r => r.GetProperty("Id").GetGuid()).Should().BeEquivalentTo(idsLegadas[perfil.Id]);
                    registros.Should().OnlyContain(r => r.GetProperty("PerfilId").GetGuid() == perfil.Id);
                    registros.Select(r => r.GetProperty("Permissao").GetString()).Should()
                        .BeEquivalentTo(PermissoesLegadas.Valores.Select(p => p.ToString()));
                    perfil.Permissoes.Should().HaveCount(i);
                }
                else
                {
                    arquivo.Should().BeNull();
                    perfil.Permissoes.Select(p => p.Id).Should().BeEquivalentTo(idsLegadas[perfil.Id]);
                }
            }
            (await db.UsuariosPerfis.IgnoreQueryFilters().CountAsync()).Should().Be(5);
            var reverter = () => db.GetService<IMigrator>().MigrateAsync("20261009170822_AddPerfilModuloInicial");
            await reverter.Should().ThrowAsync<NotSupportedException>().WithMessage("*Restaure o arquivo*");
            (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }

        var jwt = new JwtTokenService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "chave-de-teste-local-com-mais-de-32-caracteres",
        }).Build());
        for (var i = 0; i < casos.Length; i++)
        {
            string refresh;
            await using (var db = new EasyStockDbContext(options))
            {
                var repo = new UsuarioRepository(db);
                var login = new AutenticarUsuarioUseCase(repo, db, hasher, NullLogger<AutenticarUsuarioUseCase>.Instance);
                var resultado = await login.ExecuteAsync(new AutenticarUsuarioCommand(usuarios[i].Email, senha, null));
                resultado.PermissoesExplicitas.Should().Be(i < 2);
                ValidarAcesso(jwt.GerarToken(resultado), i);
                db.ChangeTracker.Clear(); // O login Google é outra requisição, sem o usuário rastreado pelo login por senha.
                var google = await login.ConcluirLoginGoogleAsync((await repo.GetByIdAsync(usuarios[i].Id))!, null);
                ValidarAcesso(jwt.GerarToken(google), i);
                refresh = Guid.NewGuid().ToString();
                db.RefreshTokens.Add(RefreshToken.Criar(usuarios[i].Id, TokenHashHelper.ComputeSha256Hash(refresh),
                    DateTime.UtcNow.AddDays(1), null, null));
                await db.SaveChangesAsync();
            }
            await using (var db = new EasyStockDbContext(options))
            {
                var renovar = new RefreshTokenUseCase(new RefreshTokenRepository(db), new UsuarioRepository(db),
                    new AuditLogRepository(db), jwt, db, NullLogger<RefreshTokenUseCase>.Instance);
                var resultado = await renovar.ExecuteAsync(new RefreshTokenCommand(refresh));
                ValidarAcesso(resultado.AccessToken, i);
            }
        }
        await using var leitura = new EasyStockDbContext(options);
        leitura.SetMobileTenantContext(empresas[0].Id);
        var atendentes = await new ListarAtendentesUseCase(new AtendenteRepository(leitura)).ExecuteAsync(empresas[0].Id);
        atendentes.Select(a => a.Nome).Should().BeEquivalentTo(["misto", "fallback"]);
    }

    private static void ValidarAcesso(string token, int caso)
    {
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims;
        var accessor = new CurrentUserAccessor(new HttpContextAccessor { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "teste")) } });
        foreach (var permissao in Enum.GetValues<Permissao>().Except(PermissoesLegadas.Valores))
            accessor.TemPermissao(permissao).Should().Be(caso == 2 || caso == 1 && permissao == Permissao.AtenderConversas);
    }
}
