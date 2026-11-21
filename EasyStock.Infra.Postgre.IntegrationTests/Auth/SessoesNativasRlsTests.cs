using Microsoft.AspNetCore.DataProtection;
using EasyStock.Infra.Postgre.Services.Auth;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Infra.Postgre.Security;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EasyStock.Api.Authentication;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Auth;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Auth;

[Collection("PostgreSqlTestCollection")]
public sealed class SessoesNativasRlsTests(PostgreSqlDatabaseFixture fixture)
{
    private const string ChaveJwt = "assinatura-sintetica-2184-sem-credencial-real-0123456789";
    private sealed record Dados(Guid Usuario, Guid A, Guid B, Guid SessaoA, Guid SessaoB);
    private static IConfiguration Config => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Jwt:SecretKey"] = ChaveJwt, ["Jwt:Issuer"] = "rls2184", ["Jwt:Audience"] = "rls2184" }).Build();

    private async Task<Dados> Semear()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetDatabaseAsync();
        await using var db = fixture.CreateDbContext();
        var a = Empresa.Criar("RLS A", null);
        var b = Empresa.Criar("RLS B", null);
        var usuario = Usuario.Criar("Ana RLS", "ana-rls@2184.test", "hash-sintetico");
        var perfilA = new Perfil { Id = Guid.NewGuid(), EmpresaId = a.Id, Nome = "Gerente A", Nivel = NivelAcesso.Gerente };
        var perfilB = new Perfil { Id = Guid.NewGuid(), EmpresaId = b.Id, Nome = "Operador B", Nivel = NivelAcesso.Operador };
        usuario.Empresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = a.Id, Ativo = true });
        usuario.Empresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = b.Id, Ativo = true });
        usuario.Perfis!.Add(new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = a.Id, PerfilId = perfilA.Id });
        usuario.Perfis.Add(new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = b.Id, PerfilId = perfilB.Id });
        var sa = new SessaoNativa { UsuarioId = usuario.Id, EmpresaId = a.Id, DispositivoId = "rls-a-2184", CriadaEm = DateTime.UtcNow.AddMinutes(-1) };
        var sb = new SessaoNativa { UsuarioId = usuario.Id, EmpresaId = b.Id, DispositivoId = "rls-b-2184", CriadaEm = DateTime.UtcNow.AddMinutes(-1) };
        db.AddRange(a, b, usuario, perfilA, perfilB, sa, sb);
        await db.SaveChangesAsync();
        return new(usuario.Id, a.Id, b.Id, sa.Id, sb.Id);
    }

    private ServiceProvider Servicos(Guid? empresa = null)
    {
        var atual = Substitute.For<ICurrentUserAccessor>();
        atual.IsAuthenticated.Returns(empresa.HasValue);
        atual.EmpresaId.Returns(empresa ?? Guid.Empty);
        atual.Nivel.Returns(NivelAcesso.Admin);
        var services = new ServiceCollection().AddLogging().AddHttpContextAccessor();
        services.AddSingleton(Config);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton(atual);
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.RlsClientConnectionString, Config);
        // Saldo de produto nÃƒÆ’Ã‚Â£o faz parte da autenticaÃƒÆ’Ã‚Â§ÃƒÆ’Ã‚Â£o; o DbContext/interceptors sÃƒÆ’Ã‚Â£o os registrados em produÃƒÆ’Ã‚Â§ÃƒÆ’Ã‚Â£o.
        services.AddSingleton(Substitute.For<IProdutoCacheInvalidator>());
        return services.BuildServiceProvider();
    }

    [SkippableFact]
    public async Task RoleComumSemTenantNaoVeSessoes_EmpresasNaoLeemNemEscrevemUmaNaOutra()
    {
        var d = await Semear();
        using var vazio = Servicos();
        using var scope = vazio.CreateScope();
        var semTenant = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var privilegiado = await semTenant.Database.SqlQueryRaw<bool>("""
            SELECT (r.rolsuper OR r.rolbypassrls OR c.relowner = r.oid) AS "Value"
            FROM pg_roles r CROSS JOIN pg_class c WHERE r.rolname = current_user AND c.relname = 'sessoes_nativas'
            """).SingleAsync();
        Assert.False(privilegiado, "A prova usa login NOSUPERUSER/NOBYPASSRLS e nÃƒÆ’Ã‚Â£o-owner.");
        Assert.Empty(await semTenant.Set<SessaoNativa>().IgnoreQueryFilters().ToListAsync());
        foreach (var empresa in new[] { d.A, d.B })
        {
            using var provider = Servicos(empresa);
            using var tenantScope = provider.CreateScope();
            var db = tenantScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var sessao = Assert.Single(await db.Set<SessaoNativa>().IgnoreQueryFilters().AsNoTracking().ToListAsync());
            Assert.Equal(empresa, sessao.EmpresaId);
            var outra = empresa == d.A ? d.B : d.A;
            var trocarTenant = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Set<SessaoNativa>().IgnoreQueryFilters()
                .Where(s => s.Id == sessao.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.EmpresaId, outra)));
            Assert.Equal("42501", trocarTenant.SqlState);
            db.Add(new SessaoNativa { UsuarioId = d.Usuario, EmpresaId = outra, DispositivoId = "tentativa-cross-tenant", CriadaEm = DateTime.UtcNow });
            var inserir = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal("42501", Assert.IsType<Npgsql.PostgresException>(inserir.InnerException).SqlState);
        }
    }

    [SkippableFact]
    public async Task GoogleRefreshLogoutEJwtFuncionamPreAuthSobRlsSemAbrirLeituraTenant()
    {
        var d = await Semear();
        using var provider = Servicos();
        LoginResponse login;
        using (var scope = provider.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<EasyStockDbContext>();
            Assert.Equal(Guid.Empty, db.CurrentTenantId);
            await db.Database.OpenConnectionAsync(); // mesma conexÃƒÂ£o antes e depois do login, sem tenant
            Assert.Equal("false", await Guc(db));
            var usuarios = sp.GetRequiredService<IUsuarioRepository>();
            var jwt = new EasyStock.Api.Services.JwtTokenService(Config);
            var controller = new AuthController(new AutenticarUsuarioUseCase(usuarios, db, Substitute.For<IPasswordHasher>(), NullLogger<AutenticarUsuarioUseCase>.Instance),
                null!, jwt, sp.GetRequiredService<IRefreshTokenRepository>(), sp.GetRequiredService<IAuditLogRepository>(), db,
                null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, sp.GetRequiredService<IRowLevelSecurityBypass>())
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            var google = Substitute.For<IGoogleIdTokenValidator>();
            google.ClientId.Returns("sintetico-2184");
            google.ValidarAsync("identidade-rls", Arg.Any<CancellationToken>()).Returns(new IdentidadeGoogle("ana-rls@2184.test", true));
            var result = Assert.IsType<OkObjectResult>(await controller.LoginGoogle(new("identidade-rls", d.A, "rls-novo-2184"), google,
                new IdentificarUsuarioGoogleUseCase(google, usuarios, null!, db, TimeProvider.System), Substitute.For<IEmpresaPadraoResolver>(), CancellationToken.None));
            var envelope = System.Text.Json.JsonSerializer.SerializeToElement(result.Value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            login = envelope.GetProperty("data").Deserialize<LoginResponse>()!;
            Assert.False(db.BypassRowLevelSecurity);
            Assert.Empty(await db.Set<SessaoNativa>().IgnoreQueryFilters().ToListAsync());
        }
        var principal = ValidarAssinatura(login.token);
        Assert.Equal(d.A.ToString(), principal.FindFirstValue("empresaId"));
        Assert.Equal("Gerente", principal.FindFirstValue("nivel"));
        using (var scope = provider.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<EasyStockDbContext>();
            Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
            Assert.True(await Validador(sp).ValidarAsync(principal));
            Assert.False(db.BypassRowLevelSecurity);
            Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
            // A mesma instância sai do pré-auth e entra no tenant A: nenhum bypass pode acompanhá-la.
            var atual = sp.GetRequiredService<ICurrentUserAccessor>();
            atual.IsAuthenticated.Returns(true);
            atual.EmpresaId.Returns(d.A);
            try
            {
                await db.Database.OpenConnectionAsync();
                Assert.Equal("false", await Guc(db));
                Assert.Empty(await db.Set<SessaoNativa>().IgnoreQueryFilters().Where(s => s.EmpresaId == d.B).ToListAsync());
                var cruzada = new SessaoNativa { UsuarioId = d.Usuario, EmpresaId = d.B, DispositivoId = "apos-jwt-tenant-b", CriadaEm = DateTime.UtcNow };
                db.Add(cruzada);
                var erro = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                Assert.Equal("42501", Assert.IsType<Npgsql.PostgresException>(erro.InnerException).SqlState);
                db.Entry(cruzada).State = EntityState.Detached;
                Assert.Equal(2, await db.Set<SessaoNativa>().IgnoreQueryFilters().CountAsync(s => s.EmpresaId == d.A));
                Assert.Equal("false", await Guc(db));
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
                atual.IsAuthenticated.Returns(false);
                atual.EmpresaId.Returns(Guid.Empty);
            }
        }
        RefreshTokenResult sucessor;
        var requisicao = Guid.NewGuid();
        using (var scope = provider.CreateScope())
        {
            var sp = scope.ServiceProvider;
            sucessor = await Renovacao(sp).ExecuteAsync(new(login.refreshToken, requisicao));
            var recuperado = await Renovacao(sp).ExecuteAsync(new(login.refreshToken, requisicao));
            Assert.Equal(sucessor.RefreshToken, recuperado.RefreshToken); // resposta perdida, recibo lido sob RLS
            Assert.Equal(d.A.ToString(), ValidarAssinatura(sucessor.AccessToken).FindFirstValue("empresaId"));
            Assert.False(sp.GetRequiredService<EasyStockDbContext>().BypassRowLevelSecurity);
        }
        using (var scope = provider.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var repo = sp.GetRequiredService<IRefreshTokenRepository>();
            var usuarios = sp.GetRequiredService<IUsuarioRepository>();
            var caso = new LogoutUseCase(repo, usuarios, new RevogadorSessoes(usuarios, repo, Substitute.For<ICacheService>(), TimeProvider.System,
                NullLogger<RevogadorSessoes>.Instance), sp.GetRequiredService<IAuditLogRepository>(), sp.GetRequiredService<IUnitOfWork>(), NullLogger<LogoutUseCase>.Instance, sp.GetRequiredService<IRowLevelSecurityBypass>());
            Assert.True((await caso.ExecuteAsync(new(login.refreshToken))).Success);
            Assert.False(await Validador(sp).ValidarAsync(principal));
            await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao(sp).ExecuteAsync(new(login.refreshToken, requisicao)));
            await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao(sp).ExecuteAsync(new(sucessor.RefreshToken)));
            var db = sp.GetRequiredService<EasyStockDbContext>();
            Assert.False(db.BypassRowLevelSecurity);
            Assert.Empty(await db.Set<SessaoNativa>().IgnoreQueryFilters().ToListAsync());
        }
    }

    [SkippableFact]
    public async Task EscopoEmConexaoAbertaRestauraGucFlagEAninhamentoMesmoAposExcecao()
    {
        var d = await Semear();
        using var provider = Servicos(d.A);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        await db.Database.OpenConnectionAsync();
        try
        {
            Assert.Equal("false", await Guc(db));
            await using (await db.UseRowLevelSecurityBypassAsync())
            {
                Assert.True(db.BypassRowLevelSecurity);
                Assert.Equal(2, await db.Set<SessaoNativa>().IgnoreQueryFilters().CountAsync());
                await using (await db.UseRowLevelSecurityBypassAsync()) Assert.Equal("true", await Guc(db));
                Assert.Equal("true", await Guc(db));
            }
            Assert.False(db.BypassRowLevelSecurity);
            Assert.Equal("false", await Guc(db));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using var rls = await db.UseRowLevelSecurityBypassAsync();
                throw new InvalidOperationException("falha sintÃƒÆ’Ã‚Â©tica dentro do escopo");
            });
            Assert.Equal("false", await Guc(db));
            Assert.False(db.BypassRowLevelSecurity);
            Assert.Equal(d.A, (await db.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync()).EmpresaId);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    [SkippableFact]
    public async Task FalhaPostgresNaoMascaraErroERollbackNaoDeixaBypassNaConexaoReusada()
    {
        var d = await Semear();
        using var provider = Servicos(d.A);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        await db.Database.OpenConnectionAsync();
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                var erro = await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
                {
                    await using var rls = await db.UseRowLevelSecurityBypassAsync();
                    Assert.Equal("true", await Guc(db));
                    await db.Database.ExecuteSqlRawAsync("SELECT 1 / 0");
                });
                Assert.Equal("22012", erro.SqlState); // preserva o erro original, nÃƒÆ’Ã‚Â£o 25P02 do Dispose
                Assert.False(db.BypassRowLevelSecurity);
                await tx.RollbackAsync();
            });
            Assert.Equal("false", await Guc(db));
            Assert.Equal(d.A, (await db.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync()).EmpresaId);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    [SkippableFact]
    public async Task RevogacaoGlobalDoUsuarioAtingeFamiliasDeAmbasEmpresasERestauraTenant()
    {
        var d = await Semear();
        using var provider = Servicos(d.A);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        await db.ExecuteInTransactionSemRetryAsync(async _ =>
        {
            await repo.RevogarSessoesAtivasAsync(d.Usuario, DateTime.UtcNow);
            Assert.Equal("false", await Guc(db));
            Assert.False(db.BypassRowLevelSecurity);
            Assert.Equal(d.A, (await db.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync()).EmpresaId);
            return true;
        });
        await using var conferir = fixture.CreateDbContext();
        Assert.Equal(2, await conferir.Set<SessaoNativa>().IgnoreQueryFilters().CountAsync(s => s.RevogadaEm != null));
    }

    [SkippableFact]
    public async Task SuperAdminDeARevogaFamiliaDeB_RepareamentoManualNaoRessuscitaSessao()
    {
        var d = await Semear();
        Guid lojaId;
        const string segredo = "segredo-sintetico-revoke-rls";
        await using (var seed = fixture.CreateDbContext())
        {
            var loja = Loja.Criar(d.B, "Loja B");
            lojaId = loja.Id;
            var refresh = RefreshToken.Criar(d.Usuario, EasyStock.Application.UseCases.Common.TokenHashHelper.ComputeSha256Hash(segredo), DateTime.UtcNow.AddDays(1), null, null);
            refresh.SessaoNativaId = d.SessaoB;
            seed.AddRange(loja, refresh, new EasyStock.Domain.Entities.Mobile.MobileDevice { Id = "rls-b-2184", EmpresaId = d.B, LojaId = loja.Id,
                ApiKeyHash = new string('0', 64), SessaoNativaId = d.SessaoB, CreatedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        using var provider = Servicos(d.A);
        using (var scope = provider.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<EasyStockDbContext>();
            var admin = Substitute.For<ICurrentUserAccessor>();
            admin.EmpresaId.Returns(d.A);
            admin.Nivel.Returns(NivelAcesso.SuperAdmin);
            var controller = new EasyStock.Api.Mobile.Controllers.DevicePairingController(db, admin, Config,
                NullLogger<EasyStock.Api.Mobile.Controllers.DevicePairingController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", d.Usuario.ToString())], "teste")) } } };
            Assert.IsType<NoContentResult>(await controller.Revoke("rls-b-2184", CancellationToken.None));
            Assert.False(db.BypassRowLevelSecurity);
            Assert.Equal(d.A, (await db.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync()).EmpresaId);
        }
        await using (var seed = fixture.CreateDbContext())
        {
            Assert.NotNull((await seed.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync(s => s.Id == d.SessaoB)).RevogadaEm);
            Assert.True((await seed.RefreshTokens.SingleAsync()).Revogado);
            Assert.Null((await seed.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync(s => s.Id == d.SessaoA)).RevogadaEm);
            // CÃƒÆ’Ã‚Â³digo manual legÃƒÆ’Ã‚Â­timo novo, mesmo fluxo que permite reconectar aparelho revogado.
            seed.Add(new EasyStock.Domain.Entities.Mobile.MobileDevice { Id = "pending-rls-b", EmpresaId = d.B, LojaId = lojaId,
                ApiKeyHash = new string('1', 64), PairingCode = "218401", PairingExpiresAt = DateTime.UtcNow.AddMinutes(5), CreatedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        using var preAuth = Servicos();
        using var pairScope = preAuth.CreateScope();
        var pairDb = pairScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var pair = new EasyStock.Api.Mobile.Controllers.DevicePairingController(pairDb, Substitute.For<ICurrentUserAccessor>(), Config,
            NullLogger<EasyStock.Api.Mobile.Controllers.DevicePairingController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var resposta = await pair.Pair(new("218401", "rls-b-2184", null), CancellationToken.None);
        Assert.IsType<OkObjectResult>(resposta.Result);
        Assert.False((await pairDb.Set<EasyStock.Domain.Entities.Mobile.MobileDevice>().SingleAsync()).Revoked);
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao(pairScope.ServiceProvider).ExecuteAsync(new(segredo)));
    }

    private static Task<string> Guc(EasyStockDbContext db) => db.Database.SqlQueryRaw<string>("SELECT current_setting('app.bypass_rls', true) AS \"Value\"").SingleAsync();

    private static RefreshTokenUseCase Renovacao(IServiceProvider sp) => new(sp.GetRequiredService<IRefreshTokenRepository>(),
        sp.GetRequiredService<IUsuarioRepository>(), sp.GetRequiredService<IAuditLogRepository>(), new EasyStock.Api.Services.JwtTokenService(Config),
        sp.GetRequiredService<IUnitOfWork>(), NullLogger<RefreshTokenUseCase>.Instance, new ProtetorRefreshNativo(sp.GetRequiredService<IDataProtectionProvider>()), rlsBypass: sp.GetRequiredService<IRowLevelSecurityBypass>());

    private static ValidadorSessaoUsuario Validador(IServiceProvider sp) => new(Substitute.For<ICacheService>(), sp.GetRequiredService<IUsuarioRepository>(),
        Config, NullLogger<ValidadorSessaoUsuario>.Instance, sp.GetRequiredService<IRefreshTokenRepository>());

    private static ClaimsPrincipal ValidarAssinatura(string token) => new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token,
        new TokenValidationParameters { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ChaveJwt)),
            ValidIssuer = "rls2184", ValidAudience = "rls2184", ValidateLifetime = true, ClockSkew = TimeSpan.Zero }, out _);
}
