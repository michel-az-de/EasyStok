using DotNet.Testcontainers.Builders;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.Services.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using System.Text.Json;
using Npgsql;

namespace EasyStock.Api.IntegrationTests.Storefront.Auth;

/// <summary>
/// Testes de integração do fluxo validar-OTP (EZ-AUTH-002, ADR-0012).
///
/// Requer Docker (Testcontainers) ou env var EASYSTOCK_IT_PG apontando para
/// Postgres externo. Sem nenhum dos dois, os testes retornam sem Assert —
/// não falham como falso-positivo.
/// </summary>
public sealed class ValidarOtpControllerTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;
    private string? _connString;
    private readonly VariaveisDeProcesso _variaveis = new();

    private const string JwtIssuer = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret = "EasyStock-Test-SuperSecretKey-Min32Chars!!";

    private const string SlugTeste = "test-storefront-validar-otp";
    private const string TelefoneE164 = "+5511987654321";
    private const string CodigoValido = "654321";

    public async Task InitializeAsync()
    {
        var externalPg = Environment.GetEnvironmentVariable("EASYSTOCK_IT_PG");
        if (!string.IsNullOrWhiteSpace(externalPg))
        {
            _connString = externalPg;
            _variaveis
                .Definir("ConnectionStrings__DefaultConnection", externalPg)
                .Definir("Database__Provider", "PostgreSql")
                .Definir("RunMigrationsOnStartup", "true")
                .Definir("Mobile__ApiKey", "easystock-integration-test-mobile-key-0001")
                .Definir("SEED_SUPERADMIN_PASSWORD", "Integr4cao-T3st-SuperAdmin");
            _isAvailable = true;
            return;
        }

        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_validarotp_tests")
                .WithUsername("postgres")
                .WithPassword("Si6IT-" + Guid.NewGuid().ToString("N"))
                .Build();
            await _pg.StartAsync();
            _connString = _pg.GetConnectionString();
            await using var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseNpgsql(_connString).Options);
            using var bypass = db.UseRowLevelSecurityBypass();
            await db.Database.MigrateAsync();
            _isAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        _variaveis.Dispose();
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    private WebApplicationFactory<Program> CriarFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.UseSetting("Database:Provider", "PostgreSql");
                b.UseSetting("ConnectionStrings:DefaultConnection", _connString);
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:Provider"] = "PostgreSql",
                        ["ConnectionStrings:DefaultConnection"] = _connString,
                        ["ConnectionStrings:Redis"] = "localhost:6379",
                        ["Jwt:Issuer"] = JwtIssuer,
                        ["Jwt:Audience"] = JwtAudience,
                        ["Jwt:SecretKey"] = JwtSecret,
                        ["Jwt:ExpirationMinutes"] = "60",
                        ["Anthropic:Enabled"] = "false",
                        ["FileStorage:Provider"] = "Local",
                        ["RunMigrationsOnStartup"] = _pg is null ? "true" : "false",
                        ["Mobile:ApiKey"] = "easystock-integration-test-mobile-key-0001",
                    });
                });
            });

    private static async Task<Guid> SeedDadosAsync(IServiceProvider services, string codigoHash)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();

        var empresa = Empresa.Criar("Empresa Teste ValidarOtp", null);
        db.Empresas.Add(empresa);

        var storefront = EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresaId: empresa.Id,
            slug: SlugTeste,
            tituloPublico: "Storefront Teste Auth",
            pedidoMinimoEntrega: 0m);
        storefront.Ativar();
        db.Storefronts.Add(storefront);

        var telefoneHash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
        var otp = ClienteOtp.Criar(
            empresaId: empresa.Id,
            telefoneHash: telefoneHash,
            codigoHash: codigoHash,
            time: TimeProvider.System);
        db.ClienteOtps.Add(otp);

        await db.SaveChangesAsync();
        return empresa.Id;
    }

    [SkippableFact]
    public async Task ValidarOtp_CodigoCorreto_Retorna200_ComCookieDeSession()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        var codigoHash = BCrypt.Net.BCrypt.HashPassword(CodigoValido);
        await SeedDadosAsync(factory.Services, codigoHash);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/storefront/{SlugTeste}/auth/validar-otp",
            new { Telefone = TelefoneE164, Codigo = CodigoValido });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookieHeader = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? string.Join("; ", cookies)
            : string.Empty;
        setCookieHeader.Should().Contain("__Host-cdb_session", "cookie de sessão deve ser setado");
        setCookieHeader.Should().Contain("httponly", because: "cookie deve ter flag HttpOnly");
        setCookieHeader.Should().Contain("samesite=lax", because: "cookie deve ter SameSite=Lax");

        var body = await response.Content.ReadFromJsonAsync<ValidarOtpResponseDto>();
        body.Should().NotBeNull();
        body!.TelefoneOfuscado.Should().Contain("*", because: "telefone deve ser ofuscado na resposta");
        body.PrimeiroNome.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task ValidarOtp_CodigoErrado_Retorna401_SemCookie()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        var codigoHash = BCrypt.Net.BCrypt.HashPassword(CodigoValido);
        await SeedDadosAsync(factory.Services, codigoHash);

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/storefront/{SlugTeste}/auth/validar-otp",
            new { Telefone = TelefoneE164, Codigo = "000000" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").Should().BeFalse("cookie não deve ser setado em erro");
    }

    [SkippableFact]
    public async Task ValidarOtp_StorefrontInexistente_Retorna404()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/storefront/slug-que-nao-existe-xyzxyz/auth/validar-otp",
            new { Telefone = TelefoneE164, Codigo = CodigoValido });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task ValidarOtp_OtpInexistente_Retorna401()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();

        // Seed apenas storefront, sem OTP
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();
        var empresa = Empresa.Criar("Empresa Sem OTP", null);
        db.Empresas.Add(empresa);
        var storefront = EasyStock.Domain.Entities.Storefront.Storefront.Criar(
            empresa.Id, SlugTeste + "-sem-otp", "Sem OTP", 0m);
        storefront.Ativar();
        db.Storefronts.Add(storefront);
        await db.SaveChangesAsync();

        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/storefront/{SlugTeste}-sem-otp/auth/validar-otp",
            new { Telefone = TelefoneE164, Codigo = CodigoValido });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// #1259: a requisição do site é anônima. Sem o tenant fixado pelo slug, a sessão recém-criada
    /// não era encontrada e "meus pedidos" devolvia 401 logo depois do login.
    /// </summary>
    [SkippableFact]
    public async Task ValidarOtp_CookieDaSessao_AbreMeusPedidos()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");

        await using var factory = CriarFactory();
        await SeedDadosAsync(factory.Services, BCrypt.Net.BCrypt.HashPassword(CodigoValido));

        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            $"/api/storefront/{SlugTeste}/auth/validar-otp",
            new { Telefone = TelefoneE164, Codigo = CodigoValido });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var cookie = login.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("__Host-cdb_session=", StringComparison.Ordinal))
            .Split(';')[0];

        using var pedidos = new HttpRequestMessage(HttpMethod.Get, $"/api/storefront/{SlugTeste}/pedidos");
        pedidos.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(pedidos);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a sessão criada no login precisa valer na requisição seguinte");
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_exige_par_csrf_e_so_revoga_com_token_valido(bool autenticado)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        await SeedDadosAsync(factory.Services, BCrypt.Net.BCrypt.HashPassword(CodigoValido));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri(autenticado ? "https://localhost" : "http://localhost") });
        Guid? sessionId = null;
        if (autenticado)
        {
            var login = await client.PostAsJsonAsync($"/api/storefront/{SlugTeste}/auth/validar-otp",
                new { Telefone = TelefoneE164, Codigo = CodigoValido });
            login.StatusCode.Should().Be(HttpStatusCode.OK);
            var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-cdb_session=", StringComparison.Ordinal));
            sessionId = Guid.Parse(cookie.Split(';')[0].Split('=')[1]);
        }
        var chatToken = AcessoChatSite.NovoToken();
        var chatHash = AcessoChatSite.HashDoToken(chatToken);
        Guid chatId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            var loja = await db.Storefronts.IgnoreQueryFilters().SingleAsync(s => s.Slug == SlugTeste);
            var chat = SessaoChatSite.Abrir(loja.EmpresaId, loja.Id, chatHash, DateTime.UtcNow);
            chatId = chat.Id;
            db.SessoesChatSite.Add(chat);
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Chat-Token", chatToken);
        client.DefaultRequestHeaders.Add("Origin", "https://outro-site.example");
        (await client.PostAsync($"/api/storefront/{SlugTeste}/auth/logout", null)).StatusCode.Should()
            .Be(autenticado ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest);
        client.DefaultRequestHeaders.Remove("Origin");
        (await client.PostAsync($"/api/storefront/{SlugTeste}/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var csrf = await client.GetAsync($"/api/storefront/{SlugTeste}/auth/csrf");
        csrf.StatusCode.Should().Be(HttpStatusCode.OK);
        csrf.Headers.CacheControl!.NoStore.Should().BeTrue();
        var token = (await csrf.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("requestToken").GetString();
        token.Should().NotBeNullOrWhiteSpace();
        client.DefaultRequestHeaders.Add("X-CSRF-Token", "token-invalido");
        (await client.PostAsync($"/api/storefront/{SlugTeste}/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var semCookie = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }))
        {
            semCookie.DefaultRequestHeaders.Add("X-Chat-Token", chatToken);
            semCookie.DefaultRequestHeaders.Add("X-CSRF-Token", token);
            (await semCookie.PostAsync($"/api/storefront/{SlugTeste}/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            (await db.SessoesChatSite.IgnoreQueryFilters().SingleAsync(s => s.Id == chatId)).TokenHash.Should().Be(chatHash);
            if (sessionId is { } id) (await db.ClienteSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == id)).Revogada.Should().BeFalse();
        }
        client.DefaultRequestHeaders.Remove("X-CSRF-Token");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", token);
        (await client.PostAsync($"/api/storefront/{SlugTeste}/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            (await db.SessoesChatSite.IgnoreQueryFilters().SingleAsync(s => s.Id == chatId)).TokenHash.Should().NotBe(chatHash);
            if (sessionId is { } id) (await db.ClienteSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == id)).Revogada.Should().BeTrue();
        }
    }

    [SkippableTheory]
    [InlineData("valido")]
    [InlineData("outro-tenant")]
    [InlineData("codigo-invalido")]
    [InlineData("corrida")]
    public async Task OTP_com_token_comanda_vincula_somente_posse_e_mesmo_tenant(string cenario)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        var empresaId = await SeedDadosAsync(factory.Services, BCrypt.Net.BCrypt.HashPassword(CodigoValido));
        var sucesso = cenario is "valido" or "corrida";
        if (cenario == "corrida")
        {
            async Task<bool> Inserir()
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                using var bypass = db.UseRowLevelSecurityBypass();
                var cliente = Cliente.Criar(empresaId, "Cadastro preservado");
                cliente.Telefone = TelefoneE164; cliente.TelefoneHash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
                db.Clientes.Add(cliente);
                try { await db.SaveChangesAsync(); return true; }
                catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505", ConstraintName: "ix_clientes_empresa_telefone_hash" }) { return false; }
            }
            (await Task.WhenAll(Inserir(), Inserir())).Should().BeEquivalentTo([true, false]);
        }
        var token = AcessoChatSite.NovoToken();
        Guid conversaId;
        string rascunho;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            var empresaLink = empresaId;
            if (cenario == "valido")
            {
                var cliente = Cliente.Criar(empresaId, "Cadastro preservado");
                cliente.Telefone = TelefoneE164; db.Clientes.Add(cliente);
            }
            if (cenario == "outro-tenant")
            {
                var outra = Empresa.Criar("Outra loja", null);
                db.Empresas.Add(outra); empresaLink = outra.Id;
            }
            var conversa = Conversa.Abrir(empresaLink, "visitante-otp", DateTime.UtcNow, canal: CanalConversa.ChatSite);
            var link = LinkCardapioConversa.Gerar(empresaLink, conversa.Id, AcessoChatSite.HashDoToken(token), DateTime.UtcNow);
            rascunho = JsonSerializer.Serialize(new RascunhoComandaCliente([new ItemPedidoCheckout(Guid.NewGuid(), 2, "Molho à parte")],
                Observacoes: "Sem talheres", Nome: "Maria", Telefone: TelefoneE164), ComandaClienteUseCase.Json);
            link.SalvarRascunho(0, rascunho, DateTime.UtcNow);
            db.AtendimentoConversas.Add(conversa); db.LinksCardapioConversa.Add(link);
            await db.SaveChangesAsync(); conversaId = conversa.Id;
        }
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/storefront/{SlugTeste}/auth/validar-otp", new
        {
            Telefone = TelefoneE164, Codigo = cenario == "codigo-invalido" ? "000000" : CodigoValido, TokenComanda = token,
        });
        response.StatusCode.Should().Be(sucesso ? HttpStatusCode.OK : cenario == "outro-tenant" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
        if (!sucesso) response.Headers.Contains("Set-Cookie").Should().BeFalse();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            var conversa = await db.AtendimentoConversas.IgnoreQueryFilters().SingleAsync(c => c.Id == conversaId);
            if (sucesso)
            {
                var cliente = await db.Clientes.IgnoreQueryFilters().SingleAsync(c => c.EmpresaId == empresaId);
                conversa.ClienteId.Should().Be(cliente.Id); cliente.Nome.Should().Be("Cadastro preservado");
            }
            else conversa.ClienteId.Should().BeNull();
            var link = await db.LinksCardapioConversa.IgnoreQueryFilters().SingleAsync(c => c.ConversaId == conversaId);
            ComandaClienteUseCase.Ler<RascunhoComandaCliente>(link.RascunhoJson).Should().BeEquivalentTo(
                ComandaClienteUseCase.Ler<RascunhoComandaCliente>(rascunho), o => o.WithStrictOrdering());
            link.Revisao.Should().Be(1); link.UsadoEm.Should().BeNull();
        }
    }

    private sealed record ValidarOtpResponseDto(string TelefoneOfuscado, string PrimeiroNome);
}
