using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.Storefront.Auth;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Infra.Async;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Auth;

/// <summary>
/// #1781: e-mail normalizado no login e contadores de tentativa sem corrida, em Postgres real.
/// Rajada paralela de senhas (ou códigos OTP) erradas não pode conferir mais vezes que o limite: a tentativa
/// é reservada num UPDATE atômico antes do BCrypt.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class LoginTentativasConcorrenciaTests(PostgreSqlDatabaseFixture fixture)
{
    private const int Rajada = 12;
    private static readonly BCryptPasswordHasher Bcrypt = new();

    [SkippableFact]
    public async Task LoginAchaOEmailComMaiusculasEEspacos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var local = $"ana-{Guid.NewGuid():N}";
        var usuario = await SemearUsuarioAsync(Usuario.Criar("Ana", $"{local}@casadababa.com", "hash"));

        await using var db = fixture.CreateDbContext();
        var achado = await new UsuarioRepository(db).GetByEmailAsync($"  {local.ToUpperInvariant()}@CasaDaBaba.COM ");

        achado.Should().NotBeNull("o login normaliza o e-mail digitado como o cadastro normaliza o gravado");
        achado!.Id.Should().Be(usuario.Id);
    }

    [SkippableFact]
    public async Task RajadaDeSenhasErradasNaoFuraOLimiteDoLogin()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var usuario = await SemearUsuarioAsync(
            Usuario.Criar("Rui", $"rui-{Guid.NewGuid():N}@casadababa.com", Bcrypt.Hash("senhaCorreta")));
        var hasher = new HasherQueConta(usuario.SenhaHash);

        var resultados = await Task.WhenAll(Enumerable.Range(0, Rajada).Select(_ => Task.Run(async () =>
        {
            await using var db = fixture.CreateDbContext();
            var login = new AutenticarUsuarioUseCase(
                new UsuarioRepository(db), db, hasher, NullLogger<AutenticarUsuarioUseCase>.Instance);
            return await Capturar(() => login.ExecuteAsync(new AutenticarUsuarioCommand(usuario.Email, "senhaErrada", null)));
        })));

        resultados.Should().AllBeOfType<CredenciaisInvalidasException>();
        hasher.ConferenciasDaSenhaReal.Should().BeLessThanOrEqualTo(Usuario.FalhasParaBloquear,
            "cada conferência da senha real exige uma tentativa reservada no banco");
        await using var leitura = fixture.CreateDbContext();
        var gravado = await leitura.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id);
        gravado.FailedLoginAttempts.Should().Be(Usuario.FalhasParaBloquear, "nenhuma falha paralela se perde");
        gravado.EstaBloqueado().Should().BeTrue();
    }

    [SkippableFact]
    public async Task ReservaDeTentativaComBloqueioVencidoRecomecaAContagem()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var semente = Usuario.Criar("Bia", $"bia-{Guid.NewGuid():N}@casadababa.com", "hash");
        semente.FailedLoginAttempts = Usuario.FalhasParaBloquear;
        semente.LockoutEnd = DateTime.UtcNow.AddMinutes(-1);
        var usuario = await SemearUsuarioAsync(semente);

        await using var db = fixture.CreateDbContext();
        var repo = new UsuarioRepository(db);
        (await repo.ReservarTentativaDeSenhaAsync(usuario.Id, DateTime.UtcNow)).Should().BeTrue();
        await repo.DevolverTentativaDeSenhaAsync(usuario.Id);

        var gravado = await db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id);
        gravado.FailedLoginAttempts.Should().Be(0, "a falha herdada de uma janela vencida não conta");
        gravado.LockoutEnd.Should().BeNull();
    }

    [SkippableFact]
    public async Task RajadaDeCodigosErradosNaoFuraOLimiteDoOtp()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        const string telefone = "(11) 99757-3992";
        var empresaId = Guid.NewGuid();
        var otp = ClienteOtp.Criar(empresaId, ClienteOtp.CalcularTelefoneHash("+5511997573992"),
            Bcrypt.Hash("123456"), TimeProvider.System);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Empresas.Add(new Empresa
            {
                Id = empresaId,
                Nome = "Empresa OTP",
                Documento = empresaId.ToString("N")[..14],
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow,
            });
            seed.ClienteOtps.Add(otp);
            await seed.SaveChangesAsync();
        }
        var storefront = Domain.Entities.Storefront.Storefront.Criar(empresaId, "sf-otp", "SF", 0m);
        storefront.Ativar();
        var hasher = new HasherQueConta(otp.CodigoHash);

        var resultados = await Task.WhenAll(Enumerable.Range(0, Rajada).Select(_ => Task.Run(async () =>
        {
            await using var db = ContextoDoTenant(empresaId);
            var storefronts = Substitute.For<IStorefrontRepository>();
            storefronts.GetBySlugAsync("sf-otp", Arg.Any<CancellationToken>()).Returns(storefront);
            var clientes = Substitute.For<IClienteStorefrontRepository>();
            var validar = new ValidarOtpUseCase(
                storefronts,
                new ClienteOtpRepository(db),
                clientes,
                Substitute.For<IClienteSessionRepository>(),
                new IdentificarClientePorTelefoneUseCase(Substitute.For<IClienteRepository>(), clientes,
                    NullLogger<IdentificarClientePorTelefoneUseCase>.Instance),
                hasher,
                db,
                TimeProvider.System,
                NullLogger<ValidarOtpUseCase>.Instance);
            return await Capturar(() => validar.ExecuteAsync(
                new ValidarOtpInput("sf-otp", telefone, "000000", null, null, null)));
        })));

        resultados.Should().OnlyContain(e => e is OtpInvalidoException || e is OtpTentativasExcedidasException);
        hasher.ConferenciasDaSenhaReal.Should().BeLessThanOrEqualTo(ClienteOtp.MaxTentativas,
            "cada conferência do código exige uma tentativa reservada no banco");
        await using var leitura = fixture.CreateDbContext();
        var gravado = await leitura.ClienteOtps.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == otp.Id);
        gravado.Tentativas.Should().Be(ClienteOtp.MaxTentativas, "nenhuma tentativa paralela se perde");
    }

    [SkippableFact]
    public async Task RajadaDeCodigosCertosConsomeOOtpUmaVezSo()
    {
        // #1781: o mesmo código certo em paralelo abria uma sessão por requisição (e, com telefone novo, a
        // segunda batia no índice único do cliente e devolvia 500). Só uma pode levar o OTP.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        const int paralelas = 4; // abaixo do limite de tentativas: só o consumo decide
        var empresaId = Guid.NewGuid();
        var otp = ClienteOtp.Criar(empresaId, ClienteOtp.CalcularTelefoneHash("+5511997573992"),
            Bcrypt.Hash("123456"), TimeProvider.System);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Empresas.Add(new Empresa
            {
                Id = empresaId,
                Nome = "Empresa OTP certo",
                Documento = empresaId.ToString("N")[..14],
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow,
            });
            seed.ClienteOtps.Add(otp);
            await seed.SaveChangesAsync();
        }
        var storefront = Domain.Entities.Storefront.Storefront.Criar(empresaId, "sf-otp-certo", "SF", 0m);
        storefront.Ativar();
        var sessoes = Substitute.For<IClienteSessionRepository>();

        var resultados = await Task.WhenAll(Enumerable.Range(0, paralelas).Select(_ => Task.Run(async () =>
        {
            await using var db = ContextoDoTenant(empresaId);
            var storefronts = Substitute.For<IStorefrontRepository>();
            storefronts.GetBySlugAsync("sf-otp-certo", Arg.Any<CancellationToken>()).Returns(storefront);
            var clientes = Substitute.For<IClienteStorefrontRepository>();
            clientes.GetByTelefoneHashAsync(empresaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Cliente.Criar(empresaId, "Cliente da loja"));
            var validar = new ValidarOtpUseCase(
                storefronts,
                new ClienteOtpRepository(db),
                clientes,
                sessoes,
                new IdentificarClientePorTelefoneUseCase(Substitute.For<IClienteRepository>(), clientes,
                    NullLogger<IdentificarClientePorTelefoneUseCase>.Instance),
                Bcrypt,
                db,
                TimeProvider.System,
                NullLogger<ValidarOtpUseCase>.Instance);
            return await Capturar(() => validar.ExecuteAsync(
                new ValidarOtpInput("sf-otp-certo", "(11) 99757-3992", "123456", null, null, null)));
        })));

        resultados.Count(e => e is null).Should().Be(1, "o OTP é de uso único");
        resultados.Where(e => e is not null).Should().AllBeOfType<OtpInvalidoException>();
        await sessoes.Received(1).AddAsync(Arg.Any<ClienteSession>(), Arg.Any<CancellationToken>());
        await using var leitura = fixture.CreateDbContext();
        (await leitura.ClienteOtps.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == otp.Id))
            .Consumido.Should().BeTrue();
    }

    private async Task<Usuario> SemearUsuarioAsync(Usuario usuario)
    {
        await using var db = fixture.CreateDbContext();
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    private EasyStockDbContext ContextoDoTenant(Guid empresaId)
    {
        var acesso = Substitute.For<ICurrentUserAccessor>();
        acesso.IsAuthenticated.Returns(true);
        acesso.EmpresaId.Returns(empresaId);
        var options = new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(fixture.ConnectionString).Options;
        return new EasyStockDbContext(options, acesso);
    }

    private static async Task<Exception?> Capturar(Func<Task> acao)
    {
        try
        {
            await acao();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>BCrypt de verdade (o tempo dele abre a janela da corrida) contando as conferências do hash real.</summary>
    private sealed class HasherQueConta(string hashReal) : IPasswordHasher
    {
        private int _conferencias;
        public int ConferenciasDaSenhaReal => Volatile.Read(ref _conferencias);

        public string Hash(string password) => Bcrypt.Hash(password);

        public bool Verify(string password, string hash)
        {
            if (hash == hashReal) Interlocked.Increment(ref _conferencias);
            return Bcrypt.Verify(password, hash);
        }
    }
}
