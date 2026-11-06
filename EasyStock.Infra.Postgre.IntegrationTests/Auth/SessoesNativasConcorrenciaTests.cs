using System.Security.Claims;
using System.Data.Common;
using EasyStock.Api.Controllers;
using EasyStock.Api.Authentication;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.AtribuirPerfilUsuario;
using EasyStock.Application.UseCases.DesativarUsuario;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Auth;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Services.Auth;
using EasyStock.Infra.Async;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Auth;

[Collection("PostgreSqlTestCollection")]
public sealed class SessoesNativasConcorrenciaTests(PostgreSqlDatabaseFixture fixture)
{
    private const string Segredo = "refresh-nativo-sintetico-2184";
    private sealed record Dados(Guid Usuario, Guid Empresa, Guid Loja, SessaoNativa Sessao);

    private async Task<Dados> Semear()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetDatabaseAsync();
        await using var db = fixture.CreateDbContext();
        var empresa = Empresa.Criar("Laboratório 2184", null);
        var usuario = Usuario.Criar("Ana", "ana@2184.test", "hash");
        usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = empresa.Id, Ativo = true });
        var loja = Loja.Criar(empresa.Id, "Loja única");
        var sessao = new SessaoNativa { UsuarioId = usuario.Id, EmpresaId = empresa.Id, DispositivoId = "android-2184", CriadaEm = DateTime.UtcNow.AddMinutes(-1) };
        var refresh = Domain.Entities.RefreshToken.Criar(usuario.Id, TokenHashHelper.ComputeSha256Hash(Segredo), DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc), null, null);
        refresh.SessaoNativaId = sessao.Id;
        var pc = Domain.Entities.RefreshToken.Criar(usuario.Id, "hash-pc-2184", DateTime.UtcNow.AddDays(1), null, null);
        db.AddRange(empresa, usuario, loja, sessao, refresh, pc);
        await db.SaveChangesAsync();
        return new(usuario.Id, empresa.Id, loja.Id, sessao);
    }

    private static RefreshTokenUseCase Renovacao(EasyStockDbContext db, IProtetorRefreshNativo? protetor = null) => new(new RefreshTokenRepository(db), new UsuarioRepository(db),
        new AuditLogRepository(db), Substitute.For<IJwtTokenService>(), db, NullLogger<RefreshTokenUseCase>.Instance, protetor);

    private ServiceProvider Protecao() => new ServiceCollection().AddLogging()
        .AddDbContext<EasyStockDbContext>(o => o.UseNpgsql(fixture.ConnectionString)).AddEasyStockDataProtection().BuildServiceProvider();

    private static IProtetorRefreshNativo Protetor(IServiceProvider servicos) => new ProtetorRefreshNativo(servicos.GetRequiredService<IDataProtectionProvider>());

    private static LogoutUseCase Saida(EasyStockDbContext db)
    {
        var tokens = new RefreshTokenRepository(db);
        var usuarios = new UsuarioRepository(db);
        return new(tokens, usuarios, new RevogadorSessoes(usuarios, tokens, Substitute.For<ICacheService>(), TimeProvider.System,
            NullLogger<RevogadorSessoes>.Instance), new AuditLogRepository(db), db, NullLogger<LogoutUseCase>.Instance);
    }

    [SkippableTheory]
    [InlineData("vinculo")]
    [InlineData("perfil")]
    [InlineData("usuario")]
    public async Task LoginGoogleEmEsperaNaoUltrapassaRevogacaoDoUsuario(string alteracao)
    {
        var dados = await Semear();
        await using var bloqueador = fixture.CreateDbContext();
        Assert.IsType<OkObjectResult>(await Parear(bloqueador, dados));
        var gerente = new Perfil { Id = Guid.NewGuid(), EmpresaId = dados.Empresa, Nome = "Gerente", Nivel = NivelAcesso.Gerente };
        var operador = new Perfil { Id = Guid.NewGuid(), EmpresaId = dados.Empresa, Nome = "Operador", Nivel = NivelAcesso.Operador };
        bloqueador.AddRange(gerente, operador, new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = dados.Usuario,
            EmpresaId = dados.Empresa, PerfilId = gerente.Id, AtribuidoEm = DateTime.UtcNow });
        await bloqueador.SaveChangesAsync();
        await using var tx = await bloqueador.Database.BeginTransactionAsync();
        await new RefreshTokenRepository(bloqueador).TravarDispositivoAsync(dados.Sessao.DispositivoId);

        var aguardando = new ObservadorTravaLogin2184();
        await using var loginDb = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(aguardando).Options);
        var login = LoginGoogle(loginDb, dados);
        try
        {
            // O controller real já identificou o usuário; sua trava PostgreSQL está retida por outra conexão.
            await aguardando.Iniciada.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var alterar = fixture.CreateDbContext();
            if (alteracao == "vinculo")
                await alterar.Set<UsuarioEmpresa>().IgnoreQueryFilters().Where(e => e.UsuarioId == dados.Usuario)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Ativo, false));
            else if (alteracao == "usuario")
                await alterar.Set<Usuario>().Where(u => u.Id == dados.Usuario).ExecuteUpdateAsync(s => s.SetProperty(u => u.Ativo, false));
            else
                await alterar.Set<UsuarioPerfil>().IgnoreQueryFilters().Where(p => p.UsuarioId == dados.Usuario)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.PerfilId, operador.Id));
            var usuarios = new UsuarioRepository(alterar);
            await new RevogadorSessoes(usuarios, new RefreshTokenRepository(alterar), Substitute.For<ICacheService>(),
                TimeProvider.System, NullLogger<RevogadorSessoes>.Instance).RevogarAsync((await usuarios.GetByIdAsync(dados.Usuario))!);
            await alterar.CommitAsync();
        }
        finally { await tx.CommitAsync(); }

        Assert.IsType<UnauthorizedObjectResult>(await login);
        await using var verificar = fixture.CreateDbContext();
        Assert.Equal(1, await verificar.Set<SessaoNativa>().IgnoreQueryFilters().CountAsync());
        await AguardarSegundoAposCorte(verificar, dados.Usuario);
        var depois = await LoginGoogle(verificar, dados);
        if (alteracao != "perfil") Assert.IsType<UnauthorizedObjectResult>(depois);
        else
        {
            Assert.IsType<OkObjectResult>(depois);
            // A nova tentativa explícita pode entrar, mas somente com o perfil atual.
            Assert.Equal(NivelAcesso.Operador, UltimoLogin2184(verificar));
        }
    }

    private readonly Dictionary<EasyStockDbContext, AutenticarUsuarioResult> _logins2184 = [];
    private NivelAcesso UltimoLogin2184(EasyStockDbContext db) => _logins2184[db].Nivel;

    private Task<IActionResult> LoginGoogle(EasyStockDbContext db, Dados dados)
    {
        var usuarios = new UsuarioRepository(db);
        var autenticar = new AutenticarUsuarioUseCase(usuarios, db, Substitute.For<IPasswordHasher>(), NullLogger<AutenticarUsuarioUseCase>.Instance);
        var jwt = Substitute.For<EasyStock.Api.Services.IJwtTokenService>();
        jwt.GerarToken(Arg.Any<AutenticarUsuarioResult>()).Returns(c => { _logins2184[db] = c.Arg<AutenticarUsuarioResult>(); return "jwt-sintetico-2184"; });
        jwt.GerarRefreshToken().Returns(_ => Guid.NewGuid().ToString());
        var controller = new AuthController(autenticar, null!, jwt, new RefreshTokenRepository(db), new AuditLogRepository(db), db,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var google = Substitute.For<IGoogleIdTokenValidator>();
        google.ClientId.Returns("google-sintetico-2184");
        google.ValidarAsync("identidade-2184", Arg.Any<CancellationToken>()).Returns(new IdentidadeGoogle("ana@2184.test", true));
        return controller.LoginGoogle(new("identidade-2184", dados.Empresa, dados.Sessao.DispositivoId), google,
            new IdentificarUsuarioGoogleUseCase(google, usuarios, null!, db, TimeProvider.System), Substitute.For<IEmpresaPadraoResolver>(), CancellationToken.None);
    }

    private sealed class ObservadorTravaLogin2184 : DbCommandInterceptor
    {
        public TaskCompletionSource Iniciada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT 1 FROM mobile_devices", StringComparison.Ordinal)) Iniciada.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    [SkippableTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LoginIniciadoEntreCorteECommitNaoRecebeAcessoAntigo(bool desativar, bool corteJaAtual)
    {
        var dados = await Semear();
        await using (var semear = fixture.CreateDbContext())
        {
            var gerente = new Perfil { Id = Guid.NewGuid(), EmpresaId = dados.Empresa, Nome = "Gerente", Nivel = NivelAcesso.Gerente };
            semear.AddRange(gerente, new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = dados.Usuario,
                EmpresaId = dados.Empresa, PerfilId = gerente.Id, AtribuidoEm = DateTime.UtcNow });
            await semear.SaveChangesAsync();
        }
        var current = Substitute.For<ICurrentUserAccessor>();
        current.EmpresaId.Returns(dados.Empresa);
        current.Nivel.Returns(NivelAcesso.Admin);
        current.IsAuthenticated.Returns(true);
        await using var admin = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.ConnectionString).Options, current);
        var operador = new Perfil { Id = Guid.NewGuid(), EmpresaId = dados.Empresa, Nome = "Operador", Nivel = NivelAcesso.Operador };
        admin.Add(operador);
        await admin.SaveChangesAsync();
        var corteExecutado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberarCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cacheReal = new RedisCacheService(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<RedisCacheService>.Instance);
        var cache = Substitute.For<ICacheService>();
        var remocoes = 0;
        cache.RemoveAsync(Arg.Any<string>()).Returns(async c =>
        {
            await cacheReal.RemoveAsync(c.Arg<string>());
            if (Interlocked.Increment(ref remocoes) == 1) { corteExecutado.TrySetResult(); await liberarCommit.Task; }
        });
        var usuarios = new UsuarioRepository(admin);
        var agora = DateTimeOffset.UtcNow;
        if (corteJaAtual)
            await usuarios.AtualizarSessoesValidasDesdeAsync(dados.Usuario, new DateTime(agora.Ticks - agora.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc));
        var revogador = new RevogadorSessoes(usuarios, new RefreshTokenRepository(admin), cache, new RelogioDoCorte2184(agora), NullLogger<RevogadorSessoes>.Instance);
        var atribuir = new AtribuirPerfilUsuarioUseCase(usuarios, new UsuarioPerfilRepository(admin), new PerfilRepository(admin), revogador,
            current, new AuditLogRepository(admin), admin, NullLogger<AtribuirPerfilUsuarioUseCase>.Instance);
        var convites = new ConvitesDeAcesso(new ResetTokenRepository(admin), null!, null!, null!, new ConfigurationBuilder().Build(),
            TimeProvider.System, NullLogger<ConvitesDeAcesso>.Instance);
        var desativarCaso = new DesativarUsuarioUseCase(usuarios, new UsuarioEmpresaRepository(admin), revogador, convites,
            current, new AuditLogRepository(admin), admin, NullLogger<DesativarUsuarioUseCase>.Instance);
        var troca = desativar ? desativarCaso.ExecuteAsync(new(dados.Usuario, dados.Empresa))
            : atribuir.ExecuteAsync(new(dados.Usuario, dados.Empresa, operador.Id, null));
        var etapa = await Task.WhenAny(corteExecutado.Task, troca).WaitAsync(TimeSpan.FromSeconds(10));
        if (etapa == troca) await troca;
        Assert.True(corteExecutado.Task.IsCompleted, "O use case precisa alcançar o corte real antes de iniciar o login concorrente.");
        await AguardarSegundoAposCorte(admin, dados.Usuario);
        var observador = new ObservadorRegistroAcesso2184();
        await using var loginDb = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(observador).Options);
        await loginDb.Database.OpenConnectionAsync();
        await using var pidCommand = loginDb.Database.GetDbConnection().CreateCommand();
        pidCommand.CommandText = "SELECT pg_backend_pid()";
        var pid = Convert.ToInt32(await pidCommand.ExecuteScalarAsync());
        var login = LoginGoogle(loginDb, dados);
        await using var leituraWeb = fixture.CreateDbContext();
        var validador = new ValidadorSessaoUsuario(cacheReal, new UsuarioRepository(leituraWeb), new ConfigurationBuilder().Build(), NullLogger<ValidadorSessaoUsuario>.Instance);
        var web = new ClaimsPrincipal(new ClaimsIdentity([new("sub", dados.Usuario.ToString()),
            new("iat", DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))], "prova"));
        try
        {
            await observador.Iniciada.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(await EsperarBloqueioDoUsuario(login, leituraWeb, pid), "O login precisa aguardar o commit administrativo mesmo se o corte já era igual.");
            if (!corteJaAtual) Assert.True(await validador.ValidarAsync(web), "Antes do commit a outra conexão ainda lê o estado anterior e preenche o cache.");
        }
        finally { liberarCommit.TrySetResult(); }
        await troca;
        if (desativar) Assert.IsType<UnauthorizedObjectResult>(await login);
        else
        {
            Assert.IsType<OkObjectResult>(await login);
            Assert.Equal(NivelAcesso.Operador, UltimoLogin2184(loginDb));
        }
        Assert.False(await validador.ValidarAsync(web), "Após o commit o cache web não pode conservar o corte anterior.");
    }

    private static async Task<bool> EsperarBloqueioDoUsuario(Task login, EasyStockDbContext db, int pid)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT cardinality(pg_blocking_pids(@pid)) > 0";
        var parametro = command.CreateParameter(); parametro.ParameterName = "pid"; parametro.Value = pid; command.Parameters.Add(parametro);
        var inicio = System.Diagnostics.Stopwatch.StartNew();
        while (!login.IsCompleted && inicio.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (await command.ExecuteScalarAsync() is true) return true;
            await Task.Delay(10);
        }
        return false;
    }

    private sealed class RelogioDoCorte2184(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private static async Task AguardarSegundoAposCorte(EasyStockDbContext db, Guid usuario)
    {
        var corte = (await new UsuarioRepository(db).ObterSessaoAsync(usuario))!.SessoesValidasDesde!.Value;
        var restante = corte.AddSeconds(1) - DateTime.UtcNow;
        if (restante > TimeSpan.Zero) await Task.Delay(restante + TimeSpan.FromMilliseconds(10));
    }

    private sealed class ObservadorRegistroAcesso2184 : DbCommandInterceptor
    {
        public TaskCompletionSource Iniciada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE usuarios", StringComparison.Ordinal)) Iniciada.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    [SkippableFact]
    public async Task RespostaPerdidaRecuperaMesmoSucessorAposRecriarProvider_MasLogoutImpedeReplay()
    {
        var dados = await Semear();
        var command = new RefreshTokenCommand(Segredo, Guid.NewGuid());
        RefreshTokenResult perdida;
        using (var servicos = Protecao())
        {
            await using var db = fixture.CreateDbContext();
            perdida = await Renovacao(db, Protetor(servicos)).ExecuteAsync(command);
        }
        // Outro provider precisa ler o key ring persistido, como após reinício da API.
        using var novosServicos = Protecao();
        var protetor = Protetor(novosServicos);
        await using var verificar = fixture.CreateDbContext();
        var sessao = await verificar.Set<SessaoNativa>().IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.NotNull(sessao.RefreshSucessorProtegido);
        Assert.DoesNotContain(perdida.RefreshToken, sessao.RefreshSucessorProtegido);
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => protetor.Desproteger(Guid.NewGuid(), sessao.RefreshSucessorProtegido));
        var alterado = sessao.RefreshSucessorProtegido.ToCharArray();
        alterado[alterado.Length / 2] = alterado[alterado.Length / 2] == 'A' ? 'B' : 'A';
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => protetor.Desproteger(dados.Sessao.Id, new string(alterado)));
        var recuperada = await Renovacao(verificar, protetor).ExecuteAsync(command);
        Assert.Equal(perdida.RefreshToken, recuperada.RefreshToken);
        Assert.Equal(2, await verificar.Set<Domain.Entities.RefreshToken>().CountAsync(r => r.SessaoNativaId == dados.Sessao.Id));
        await Saida(verificar).ExecuteAsync(new(Segredo));
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao(verificar, protetor).ExecuteAsync(command));
    }

    [SkippableFact]
    public async Task MesmoNonceConcorrenteRetornaMesmoSucessorSemDuplicarRotacao()
    {
        var dados = await Semear();
        using var servicos = Protecao();
        var protetor = Protetor(servicos);
        var command = new RefreshTokenCommand(Segredo, Guid.NewGuid());
        async Task<RefreshTokenResult> Tentar()
        {
            await using var db = fixture.CreateDbContext();
            return await Renovacao(db, protetor).ExecuteAsync(command);
        }
        var respostas = await Task.WhenAll(Tentar(), Tentar());
        Assert.Equal(respostas[0].RefreshToken, respostas[1].RefreshToken);
        await using var verificar = fixture.CreateDbContext();
        Assert.Equal(2, await verificar.Set<Domain.Entities.RefreshToken>().CountAsync(r => r.SessaoNativaId == dados.Sessao.Id));
        Assert.Equal(1, await verificar.Set<Domain.Entities.RefreshToken>().CountAsync(r => r.SessaoNativaId == dados.Sessao.Id && !r.Revogado));
    }

    [SkippableFact]
    public async Task FalhaDepoisDoReciboReverteConsumoESucessor_ParaRepetirMesmoNonce()
    {
        var dados = await Semear();
        using var servicos = Protecao();
        var protetor = Protetor(servicos);
        var command = new RefreshTokenCommand(Segredo, Guid.NewGuid());
        await using (var db = fixture.CreateDbContext())
        {
            var jwt = Substitute.For<IJwtTokenService>();
            jwt.GerarToken(Arg.Any<EasyStock.Application.UseCases.AutenticarUsuario.AutenticarUsuarioResult>())
                .Returns(_ => throw new InvalidOperationException("Falha sintética após persistir recibo na transação."));
            var useCase = new RefreshTokenUseCase(new RefreshTokenRepository(db), new UsuarioRepository(db), new AuditLogRepository(db),
                jwt, db, NullLogger<RefreshTokenUseCase>.Instance, protetor);
            await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(command));
        }
        await using var verificar = fixture.CreateDbContext();
        Assert.Null((await verificar.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync()).UltimaRenovacaoId);
        Assert.False((await verificar.Set<Domain.Entities.RefreshToken>().SingleAsync(r => r.SessaoNativaId == dados.Sessao.Id)).Revogado);
        Assert.NotNull(await Renovacao(verificar, protetor).ExecuteAsync(command));
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReplayESairConcorrentesNuncaRessuscitamFamilia(bool repetirPrimeiro)
    {
        var dados = await Semear();
        using var servicos = Protecao();
        var protetor = Protetor(servicos);
        var command = new RefreshTokenCommand(Segredo, Guid.NewGuid());
        await using (var db = fixture.CreateDbContext()) await Renovacao(db, protetor).ExecuteAsync(command);
        await using var primeira = fixture.CreateDbContext();
        await using var tx = await primeira.Database.BeginTransactionAsync();
        await new RefreshTokenRepository(primeira).ObterSessaoNativaComLockAsync(dados.Sessao.Id);
        var iniciada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outra = Task.Run(async () =>
        {
            await using var db = fixture.CreateDbContext();
            iniciada.SetResult();
            if (repetirPrimeiro) await Saida(db).ExecuteAsync(new(Segredo));
            else await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao(db, protetor).ExecuteAsync(command));
        });
        await iniciada.Task;
        if (repetirPrimeiro) await Renovacao(primeira, protetor).ExecuteAsync(command);
        else await Saida(primeira).ExecuteAsync(new(Segredo));
        await tx.CommitAsync();
        await outra;
        await using var verificar = fixture.CreateDbContext();
        Assert.NotNull((await verificar.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync()).RevogadaEm);
        Assert.False(await verificar.Set<Domain.Entities.RefreshToken>().AnyAsync(r => r.SessaoNativaId == dados.Sessao.Id && !r.Revogado));
        Assert.Equal(2, await verificar.Set<Domain.Entities.RefreshToken>().CountAsync(r => r.SessaoNativaId == dados.Sessao.Id));
    }

    [SkippableFact]
    public async Task DuasConexoesSoConseguemUmSucessorDoMesmoRefresh()
    {
        var dados = await Semear();
        async Task<bool> Tentar()
        {
            await using var db = fixture.CreateDbContext();
            try { await Renovacao(db).ExecuteAsync(new(Segredo)); return true; }
            catch (CredenciaisInvalidasException) { return false; }
        }
        var respostas = await Task.WhenAll(Tentar(), Tentar());
        Assert.Single(respostas, ok => ok);
        await using var verificar = fixture.CreateDbContext();
        Assert.Equal(1, await verificar.Set<Domain.Entities.RefreshToken>().CountAsync(t => t.SessaoNativaId == dados.Sessao.Id && !t.Revogado));
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefreshESairNasDuasOrdensNaoRessuscitamSessao(bool renovarPrimeiro)
    {
        var dados = await Semear();
        await using var primeira = fixture.CreateDbContext();
        await using var tx = await primeira.Database.BeginTransactionAsync();
        await new RefreshTokenRepository(primeira).ObterSessaoNativaComLockAsync(dados.Sessao.Id);
        var outraIniciada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outra = Task.Run(async () =>
        {
            await using var db = fixture.CreateDbContext();
            outraIniciada.SetResult();
            if (renovarPrimeiro) await Saida(db).ExecuteAsync(new(Segredo));
            else await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao(db).ExecuteAsync(new(Segredo)));
        });
        await outraIniciada.Task;
        if (renovarPrimeiro) await Renovacao(primeira).ExecuteAsync(new(Segredo));
        else await Saida(primeira).ExecuteAsync(new(Segredo));
        await tx.CommitAsync();
        await outra;
        await using var verificar = fixture.CreateDbContext();
        Assert.NotNull((await verificar.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync()).RevogadaEm);
        Assert.False(await verificar.Set<Domain.Entities.RefreshToken>().AnyAsync(t => t.SessaoNativaId == dados.Sessao.Id && !t.Revogado));
        Assert.False((await verificar.Set<Domain.Entities.RefreshToken>().SingleAsync(t => t.SessaoNativaId == null)).Revogado);
    }

    [SkippableFact]
    public async Task NovaFamiliaTrocaChave_LogoutAntigoNaoRevogaNovoLogin()
    {
        var dados = await Semear();
        await using var db = fixture.CreateDbContext();
        var resultado = Assert.IsType<OkObjectResult>(await Parear(db, dados));
        var par = Assert.IsType<PairResponse>(resultado.Value);
        await Saida(db).ExecuteAsync(new(Segredo));
        var nova = new SessaoNativa { UsuarioId = dados.Usuario, EmpresaId = dados.Empresa, DispositivoId = dados.Sessao.DispositivoId, CriadaEm = DateTime.UtcNow };
        db.Add(nova);
        await db.SaveChangesAsync();
        var atualizado = Assert.IsType<OkObjectResult>(await Parear(db, dados with { Sessao = nova }, chave: par.ApiKey));
        var novoPar = Assert.IsType<PairResponse>(atualizado.Value);
        Assert.NotEqual(par.ApiKey, novoPar.ApiKey);
        await Saida(db).ExecuteAsync(new(Segredo));
        await using var verificar = fixture.CreateDbContext();
        var dispositivo = await verificar.Set<MobileDevice>().IgnoreQueryFilters().SingleAsync();
        Assert.False(dispositivo.Revoked);
        Assert.Equal(nova.Id, dispositivo.SessaoNativaId);
        Assert.Equal(TokenHashHelper.ComputeSha256Hash(novoPar.ApiKey), dispositivo.ApiKeyHash);
        Assert.False(await verificar.Set<MobileDevice>().IgnoreQueryFilters().AnyAsync(d => d.ApiKeyHash == TokenHashHelper.ComputeSha256Hash(par.ApiKey)));
        Assert.Null((await verificar.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync(s => s.Id == nova.Id)).RevogadaEm);
    }

    [SkippableFact]
    public async Task AdocaoLegadaEMesmaFamiliaPreservamChaveComprovada()
    {
        var dados = await Semear();
        await using var db = fixture.CreateDbContext();
        const string chaveLegada = "chave-legada-sintetica-2184";
        db.Add(new MobileDevice { Id = dados.Sessao.DispositivoId, EmpresaId = dados.Empresa, LojaId = dados.Loja,
            ApiKeyHash = TokenHashHelper.ComputeSha256Hash(chaveLegada), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var adocao = Assert.IsType<OkObjectResult>(await Parear(db, dados, chave: chaveLegada));
        Assert.Equal(chaveLegada, Assert.IsType<PairResponse>(adocao.Value).ApiKey);
        var repeticao = Assert.IsType<OkObjectResult>(await Parear(db, dados, chave: chaveLegada));
        Assert.Equal(chaveLegada, Assert.IsType<PairResponse>(repeticao.Value).ApiKey);
        Assert.Equal(dados.Sessao.Id, (await db.Set<MobileDevice>().IgnoreQueryFilters().SingleAsync()).SessaoNativaId);
    }

    [SkippableFact]
    public async Task LoginEmEsperaNaoCriaFamiliaAposRevogacaoAdministrativa()
    {
        var dados = await Semear();
        await using var primeira = fixture.CreateDbContext();
        Assert.IsType<OkObjectResult>(await Parear(primeira, dados));
        await using var tx = await primeira.Database.BeginTransactionAsync();
        await new RefreshTokenRepository(primeira).TravarDispositivoAsync(dados.Sessao.DispositivoId);
        var iniciada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var login = Task.Run(async () =>
        {
            await using var db = fixture.CreateDbContext();
            return await db.ExecuteInTransactionSemRetryAsync(async _ =>
            {
                var tokens = new RefreshTokenRepository(db);
                iniciada.SetResult();
                await tokens.TravarDispositivoAsync(dados.Sessao.DispositivoId);
                return !await tokens.DispositivoRevogadoAsync(dados.Sessao.DispositivoId);
            });
        });
        await iniciada.Task;
        Assert.IsType<NoContentResult>(await Pareador(primeira, dados).Revoke(dados.Sessao.DispositivoId, CancellationToken.None));
        await tx.CommitAsync();
        Assert.False(await login);
    }

    [SkippableFact]
    public async Task RevogacaoEmEsperaIncluiFamiliaQueLoginAcabaDeCriar()
    {
        var dados = await Semear();
        await using var primeira = fixture.CreateDbContext();
        Assert.IsType<OkObjectResult>(await Parear(primeira, dados));
        await using var tx = await primeira.Database.BeginTransactionAsync();
        await new RefreshTokenRepository(primeira).TravarDispositivoAsync(dados.Sessao.DispositivoId);
        var nova = new SessaoNativa { UsuarioId = dados.Usuario, EmpresaId = dados.Empresa, DispositivoId = dados.Sessao.DispositivoId, CriadaEm = DateTime.UtcNow };
        primeira.Add(nova);
        await primeira.SaveChangesAsync();
        var iniciada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revogar = Task.Run(async () =>
        {
            await using var db = fixture.CreateDbContext();
            iniciada.SetResult();
            return await Pareador(db, dados).Revoke(dados.Sessao.DispositivoId, CancellationToken.None);
        });
        await iniciada.Task;
        await tx.CommitAsync();
        Assert.IsType<NoContentResult>(await revogar);
        await using var verificar = fixture.CreateDbContext();
        Assert.NotNull((await verificar.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync(s => s.Id == nova.Id)).RevogadaEm);
    }

    [SkippableFact]
    public async Task RevogarEReparearManualmenteNaoRessuscitaFamiliaNativaNemPrendeChaveNova()
    {
        var dados = await Semear();
        await using var db = fixture.CreateDbContext();
        Assert.IsType<OkObjectResult>(await Parear(db, dados));
        var controller = Pareador(db, dados);
        Assert.IsType<NoContentResult>(await controller.Revoke(dados.Sessao.DispositivoId, CancellationToken.None));
        db.Add(new MobileDevice { Id = "pending-2184", EmpresaId = dados.Empresa, LojaId = dados.Loja, ApiKeyHash = new string('0', 64),
            PairingCode = "123456", PairingExpiresAt = DateTime.UtcNow.AddMinutes(5), PairedByUserId = dados.Usuario });
        await db.SaveChangesAsync();
        var parManual = await controller.Pair(new("123456", dados.Sessao.DispositivoId, null), CancellationToken.None);
        Assert.IsType<OkObjectResult>(parManual.Result);
        await using var verificar = fixture.CreateDbContext();
        var sessao = await verificar.Set<SessaoNativa>().IgnoreQueryFilters().SingleAsync();
        Assert.NotNull(sessao.RevogadaEm);
        Assert.Null((await verificar.Set<MobileDevice>().IgnoreQueryFilters().SingleAsync()).SessaoNativaId);
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Renovacao(verificar).ExecuteAsync(new(Segredo)));
    }

    [SkippableFact]
    public async Task PareamentoRecusaLojaAmbiguaChaveErradaERevogacaoAdministrativa()
    {
        var dados = await Semear();
        await using var db = fixture.CreateDbContext();
        db.Add(Loja.Criar(dados.Empresa, "Outra loja"));
        await db.SaveChangesAsync();
        var ambiguo = Assert.IsType<ConflictObjectResult>(await Parear(db, dados));
        Assert.Contains("LOJA_REQUIRED", System.Text.Json.JsonSerializer.Serialize(ambiguo.Value));
        Assert.IsType<UnauthorizedObjectResult>(await Parear(db, dados, dados.Loja, "chave-incorreta"));
        Assert.IsType<OkObjectResult>(await Parear(db, dados, dados.Loja));
        var device = await db.Set<MobileDevice>().IgnoreQueryFilters().SingleAsync();
        device.Revoked = true;
        await db.SaveChangesAsync();
        var revogado = Assert.IsType<ConflictObjectResult>(await Parear(db, dados, dados.Loja));
        Assert.Contains("DISPOSITIVO_REVOGADO", System.Text.Json.JsonSerializer.Serialize(revogado.Value));
    }

    private static Task<IActionResult> Parear(EasyStockDbContext db, Dados dados, Guid? loja = null, string? chave = null)
        => Pareador(db, dados, chave).PairSession(new(dados.Sessao.DispositivoId, loja), new RefreshTokenRepository(db), db, CancellationToken.None);

    private static DevicePairingController Pareador(EasyStockDbContext db, Dados dados, string? chave = null)
    {
        var current = Substitute.For<ICurrentUserAccessor>();
        current.EmpresaId.Returns(dados.Empresa);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", dados.Usuario.ToString()), new Claim(ClaimTypes.NameIdentifier, dados.Usuario.ToString()),
            new Claim("sid", dados.Sessao.Id.ToString()), new Claim("sessaoNativa", "true"), new Claim("dispositivoId", dados.Sessao.DispositivoId)], "prova")) };
        if (chave is not null) http.Request.Headers[MobileAuth.HeaderName] = chave;
        return new DevicePairingController(db, current, new ConfigurationBuilder().Build(), NullLogger<DevicePairingController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = http } };
    }
}
