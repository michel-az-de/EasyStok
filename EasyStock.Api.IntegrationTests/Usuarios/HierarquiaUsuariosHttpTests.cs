using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Auth;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests.Usuarios;

/// <summary>
/// #2070 ponta a ponta: API real (WebApplicationFactory) e Postgres real (Testcontainers). A matriz HTTP reduzida da
/// hierarquia (403 HIERARQUIA para a Administração sobre a dona e outra Administração, o que a dona e o suporte podem,
/// o ator rebaixado com JWT antigo), o refresh do suporte (M3), o Google só com convite aberto e sem o bloqueio por senha
/// (A3, B4) e o perfil de empresa com nível SuperAdmin barrado em <c>api/admin/*</c> (B1). O validador do Google é falso:
/// nada sai para a rede.
/// </summary>
public sealed class HierarquiaUsuariosHttpTests : IAsyncLifetime
{
    private const string JwtIssuer = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret = "EasyStock-Test-SuperSecretKey-Min32Chars!!";
    private const string Senha = "Senha-de-teste-2070!";

    private PostgreSqlContainer? _pg;
    private WebApplicationFactory<Program>? _factory;
    private bool _isAvailable;
    private readonly VariaveisDeProcesso _variaveis = new();
    private readonly GoogleFalso _google = new();

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_hierarquia_e2e")
                .WithUsername("postgres")
                .WithPassword("e2e-hierarquia-senha")
                .Build();
            await _pg.StartAsync();
            _isAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
            return;
        }

        var config = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["ConnectionStrings:DefaultConnection"] = _pg.GetConnectionString(),
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["Jwt:Issuer"] = JwtIssuer,
            ["Jwt:Audience"] = JwtAudience,
            ["Jwt:SecretKey"] = JwtSecret,
            ["Jwt:ExpirationMinutes"] = "60",
            ["Anthropic:Enabled"] = "false",
            ["FileStorage:Provider"] = "Local",
            ["MercadoPago:UseStub"] = "true",
            ["Auth:LinkConvite"] = "https://app.exemplo.test/#/convite?t={0}",
        };
        // ConfigureAppConfiguration chega tarde para o Program.cs ler a connection string (#1890).
        _variaveis
            .Definir("ConnectionStrings__DefaultConnection", _pg.GetConnectionString())
            .Definir("Database__Provider", "PostgreSql");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Database:Provider", "PostgreSql");
            b.UseSetting("ConnectionStrings:DefaultConnection", _pg.GetConnectionString());
            b.UseSetting("Jwt:SecretKey", JwtSecret);
            b.UseSetting("Jwt:Issuer", JwtIssuer);
            b.UseSetting("Jwt:Audience", JwtAudience);
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(config));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IGoogleIdTokenValidator>();
                s.AddSingleton<IGoogleIdTokenValidator>(_google);
            });
        });
        _ = _factory.Services; // sobe a API (migrações e seeds) uma vez para a classe
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        _variaveis.Dispose();
        if (_pg is not null) await _pg.DisposeAsync();
    }

    // ── matriz HTTP reduzida ──────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Administracao_recebe_403_HIERARQUIA_sobre_a_dona_e_outra_Administracao()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        var e = await SemearAsync();
        var http = Cliente(e.EmpresaId, e.Gestora, NivelAcesso.Admin);

        foreach (var alvo in new[] { e.DonaConvidada, e.AdmConvidada })
        {
            await DeveSer(await http.PutAsJsonAsync($"/api/usuarios/{alvo}",
                new { usuarioId = alvo, nome = "Outro nome", email = $"invasor-{Guid.NewGuid():N}@exemplo.test" }), 403, "HIERARQUIA");
            await DeveSer(await http.PutAsJsonAsync($"/api/usuarios/{alvo}/perfis",
                new { empresaId = e.EmpresaId, perfilId = e.PerfilGerencia }), 403, "HIERARQUIA");
            await DeveSer(await http.DeleteAsync($"/api/usuarios/{alvo}?empresaId={e.EmpresaId}"), 403, "HIERARQUIA");
            await DeveSer(await http.PostAsync($"/api/usuarios/{alvo}/convite", null), 403, "HIERARQUIA");
        }
        await DeveSer(await http.DeleteAsync($"/api/usuarios/{e.Dona}?empresaId={e.EmpresaId}"), 403, "HIERARQUIA");

        await using var db = Banco();
        (await db.UsuariosEmpresas.IgnoreQueryFilters().CountAsync(v => v.EmpresaId == e.EmpresaId && !v.Ativo))
            .Should().Be(0, "nenhum vínculo caiu");
        (await db.Usuarios.IgnoreQueryFilters().Where(u => u.Id == e.AdmConvidada).Select(u => u.Email).SingleAsync())
            .Should().StartWith("adm-convidada-", "A1: o e-mail da convidada não foi trocado");
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.EmpresaId == e.EmpresaId && a.Acao == "hierarquia-recusada" && !a.Sucesso))
            .Should().Be(9);
    }

    [SkippableFact]
    public async Task Dona_e_suporte_fazem_o_que_a_matriz_diz()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        var e = await SemearAsync();
        var dona = Cliente(e.EmpresaId, e.Dona, NivelAcesso.Admin);

        await DeveSer(await dona.PutAsJsonAsync($"/api/usuarios/{e.AdmConvidada}/perfis",
            new { empresaId = e.EmpresaId, perfilId = e.PerfilGerencia }), 204);
        await DeveSer(await dona.PostAsync($"/api/usuarios/{e.AdmConvidada}/convite", null), 204);
        await DeveSer(await dona.DeleteAsync($"/api/usuarios/{e.Operador}?empresaId={e.EmpresaId}"), 204);
        await DeveSer(await dona.PutAsJsonAsync($"/api/usuarios/{e.Gestora}/dona", new { dona = true }), 204);
        await DeveSer(await dona.PutAsJsonAsync($"/api/usuarios/{e.DonaConvidada}",
            new { usuarioId = e.DonaConvidada, nome = "Nova", email = $"outra-{Guid.NewGuid():N}@exemplo.test" }), 403, "HIERARQUIA");
        await DeveSer(await dona.DeleteAsync($"/api/usuarios/{e.DonaConvidada}?empresaId={e.EmpresaId}"), 409, "DONA_PROTEGIDA");
        await DeveSer(await dona.PutAsJsonAsync($"/api/usuarios/{e.Dona}/dona", new { dona = false }), 400, "USUARIO_PROPRIO");

        var suporte = Cliente(e.EmpresaId, e.Suporte, NivelAcesso.SuperAdmin);
        await DeveSer(await suporte.PutAsJsonAsync($"/api/usuarios/{e.Dona}/dona", new { dona = false }), 400, "MOTIVO_OBRIGATORIO");
        await DeveSer(await suporte.PutAsJsonAsync($"/api/usuarios/{e.Dona}/dona",
            new { dona = false, motivo = "pedido formal da titular" }), 204);

        await using var db = Banco();
        (await db.UsuariosEmpresas.IgnoreQueryFilters().Where(v => v.EmpresaId == e.EmpresaId && v.EhDona)
                .Select(v => v.UsuarioId).ToListAsync())
            .Should().BeEquivalentTo([e.DonaConvidada, e.Gestora]);
        var plataforma = await db.AdminAuditLogs.SingleAsync(a => a.EntidadeAfetadaId == e.Dona);
        plataforma.Acao.Should().Be("SuporteDesmarcouDona");
        plataforma.Motivo.Should().Be("pedido formal da titular");
    }

    [SkippableFact]
    public async Task Ator_rebaixado_com_JWT_antigo_recebe_403()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        var e = await SemearAsync();
        var http = Cliente(e.EmpresaId, e.Gestora, NivelAcesso.Admin); // o JWT foi emitido quando ela era Administração
        await using (var db = Banco())
        {
            var perfil = await db.UsuariosPerfis.IgnoreQueryFilters().SingleAsync(p => p.UsuarioId == e.Gestora && p.EmpresaId == e.EmpresaId);
            perfil.PerfilId = e.PerfilGerencia; // rebaixada direto no banco, sem o corte de sessão
            await db.SaveChangesAsync();
        }

        await DeveSer(await http.DeleteAsync($"/api/usuarios/{e.Operador}?empresaId={e.EmpresaId}"), 403, "HIERARQUIA");
    }

    [SkippableFact]
    public async Task Lista_devolve_a_marca_e_as_acoes_e_a_API_aceita_so_o_que_esta_nelas()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        var e = await SemearAsync();
        var http = Cliente(e.EmpresaId, e.Gestora, NivelAcesso.Admin);

        var corpo = await http.GetFromJsonAsync<JsonElement>($"/api/usuarios?empresaId={e.EmpresaId}&pageSize=50");
        var linhas = corpo.GetProperty("data").EnumerateArray().ToDictionary(l => l.GetProperty("usuarioId").GetGuid());

        linhas[e.Dona].GetProperty("dona").GetBoolean().Should().BeTrue();
        linhas[e.Dona].GetProperty("acoes").GetProperty("desativar").GetBoolean().Should().BeFalse();
        linhas[e.Operador].GetProperty("dona").GetBoolean().Should().BeFalse();
        linhas[e.Operador].GetProperty("acoes").GetProperty("desativar").GetBoolean().Should().BeTrue();

        await DeveSer(await http.DeleteAsync($"/api/usuarios/{e.Dona}?empresaId={e.EmpresaId}"), 403, "HIERARQUIA");
        await DeveSer(await http.DeleteAsync($"/api/usuarios/{e.Operador}?empresaId={e.EmpresaId}"), 204);
    }

    // ── M3 e B1: a sessão do suporte e o perfil de empresa com nível SuperAdmin ──────────────

    [SkippableFact]
    public async Task Refresh_do_suporte_volta_SuperAdmin_na_mesma_empresa()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        var e = await SemearAsync();
        var email = await EmailAsync(e.Suporte);
        var http = _factory!.CreateClient();

        var login = await DadosAsync(await http.PostAsJsonAsync("/api/auth/login", new { email, senha = Senha, empresaId = e.EmpresaId }));
        var refresh = await DadosAsync(await http.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = login.GetProperty("refreshToken").GetString() }));

        var claims = Claims(refresh.GetProperty("accessToken").GetString()!);
        claims["nivel"].Should().Be("SuperAdmin", "antes o refresh do suporte voltava Visualizador (M3)");
        claims["empresaId"].Should().Be(e.EmpresaId.ToString());
    }

    [SkippableFact]
    public async Task Perfil_de_empresa_com_nivel_SuperAdmin_entra_como_Administracao_e_recebe_403_em_api_admin()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        var e = await SemearAsync();
        var email = await EmailAsync(e.PerfilSuperAdminDaEmpresa);
        var http = _factory!.CreateClient();

        var login = await DadosAsync(await http.PostAsJsonAsync("/api/auth/login", new { email, senha = Senha, empresaId = e.EmpresaId }));

        login.GetProperty("usuario").GetProperty("nivel").GetString().Should().Be("Admin");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("token").GetString());
        (await http.GetAsync("/api/admin/tenants")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── A3 e B4: o Google só consome convite aberto e não sofre o bloqueio por senha ──────────

    [SkippableFact]
    public async Task Google_com_convite_aberto_entra_e_consome_o_convite()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        var e = await SemearAsync();
        await ConviteAsync(e.AdmConvidada, expiraEm: DateTime.UtcNow.AddHours(1), usado: false);

        var resposta = await EntrarPeloGoogleAsync(await EmailAsync(e.AdmConvidada));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        await using var db = Banco();
        var usuario = await db.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Id == e.AdmConvidada);
        usuario.ConvitePendente.Should().BeFalse();
        usuario.ConviteAceitoVia.Should().Be("Google");
        (await db.ResetTokens.CountAsync(t => t.UsuarioId == e.AdmConvidada && !t.Usado)).Should().Be(0);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Google_com_convite_vencido_ou_revogado_recebe_401(bool revogado)
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        var e = await SemearAsync();
        await ConviteAsync(e.AdmConvidada,
            expiraEm: revogado ? DateTime.UtcNow.AddHours(1) : DateTime.UtcNow.AddMinutes(-1), usado: revogado);

        var resposta = await EntrarPeloGoogleAsync(await EmailAsync(e.AdmConvidada));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("Seu convite venceu: peça um novo.");
        await using var db = Banco();
        (await db.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Id == e.AdmConvidada)).ConvitePendente.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Senha_errada_cinco_vezes_nao_barra_o_Google()
    {
        Skip.IfNot(_isAvailable, "Docker indisponível");
        var e = await SemearAsync();
        var email = await EmailAsync(e.Operador);
        var http = _factory!.CreateClient();
        for (var i = 0; i < 5; i++)
            await http.PostAsJsonAsync("/api/auth/login", new { email, senha = "senha-errada", empresaId = e.EmpresaId });
        await using (var db = Banco())
            (await db.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Id == e.Operador)).EstaBloqueado()
                .Should().BeTrue("cinco senhas erradas bloqueiam o login por senha");

        var resposta = await EntrarPeloGoogleAsync(email);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
    }

    // ── cenário ───────────────────────────────────────────────────────────────────────────────

    private sealed record Empresa2070(
        Guid EmpresaId, Guid Dona, Guid DonaConvidada, Guid Gestora, Guid AdmConvidada, Guid Operador, Guid Suporte,
        Guid PerfilSuperAdminDaEmpresa, Guid PerfilGerencia);

    private async Task<Empresa2070> SemearAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(Senha);
        using var _ = db.UseRowLevelSecurityBypass();
        var agora = DateTime.UtcNow;
        var empresa = new Empresa { Id = Guid.NewGuid(), Nome = "Hierarquia E2E", CriadoEm = agora, AlteradoEm = agora };
        db.Empresas.Add(empresa);
        Perfil NovoPerfil(string nome, NivelAcesso nivel)
        {
            var perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = nome, Nivel = nivel, CriadoEm = agora };
            db.Perfis.Add(perfil);
            return perfil;
        }
        var administracao = NovoPerfil("Administração", NivelAcesso.Admin);
        var gerencia = NovoPerfil("Gerência", NivelAcesso.Gerente);
        var operacao = NovoPerfil("Atendimento", NivelAcesso.Operador);
        var superAdminDaEmpresa = NovoPerfil("Perfil inválido", NivelAcesso.SuperAdmin);

        Guid Pessoa(string nome, Perfil perfil, bool dona = false, bool convidada = false)
        {
            var email = $"{nome}-{Guid.NewGuid():N}@exemplo.test";
            var usuario = convidada ? Usuario.CriarConvidado(nome, email) : Usuario.Criar(nome, email, hash);
            usuario.EmailConfirmado = !convidada;
            db.Usuarios.Add(usuario);
            db.UsuariosEmpresas.Add(new UsuarioEmpresa
            {
                Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresa.Id, Ativo = true, EhDona = dona, CriadoEm = agora,
            });
            db.UsuariosPerfis.Add(new UsuarioPerfil
            {
                Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresa.Id, PerfilId = perfil.Id, AtribuidoEm = agora,
            });
            return usuario.Id;
        }

        var ids = new
        {
            Dona = Pessoa("dona", administracao, dona: true),
            DonaConvidada = Pessoa("dona-convidada", administracao, dona: true, convidada: true),
            Gestora = Pessoa("gestora", administracao),
            AdmConvidada = Pessoa("adm-convidada", administracao, convidada: true),
            Operador = Pessoa("operador", operacao),
            PerfilInvalido = Pessoa("perfil-invalido", superAdminDaEmpresa),
        };

        var perfilDoSuporte = await db.Perfis.IgnoreQueryFilters().FirstOrDefaultAsync(Perfil.SuporteGlobal);
        if (perfilDoSuporte is null)
        {
            perfilDoSuporte = new Perfil { Id = Guid.NewGuid(), Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin, CriadoEm = agora };
            db.Perfis.Add(perfilDoSuporte);
        }
        var suporte = Usuario.Criar("Suporte", $"suporte-{Guid.NewGuid():N}@exemplo.test", hash);
        suporte.EmailConfirmado = true;
        db.Usuarios.Add(suporte);
        db.UsuariosPerfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = suporte.Id, EmpresaId = Guid.Empty, PerfilId = perfilDoSuporte.Id, AtribuidoEm = agora,
        });
        await db.SaveChangesAsync();

        return new Empresa2070(empresa.Id, ids.Dona, ids.DonaConvidada, ids.Gestora, ids.AdmConvidada, ids.Operador, suporte.Id,
            ids.PerfilInvalido, gerencia.Id);
    }

    private async Task ConviteAsync(Guid usuarioId, DateTime expiraEm, bool usado)
    {
        await using var db = Banco();
        var token = ResetToken.Criar(usuarioId, $"hash-{Guid.NewGuid():N}", expiraEm, null, null, FinalidadeResetToken.Convite, canal: "Email");
        token.Usado = usado;
        db.ResetTokens.Add(token);
        await db.SaveChangesAsync();
    }

    private async Task<string> EmailAsync(Guid usuarioId)
    {
        await using var db = Banco();
        return await db.Usuarios.IgnoreQueryFilters().Where(u => u.Id == usuarioId).Select(u => u.Email).SingleAsync();
    }

    private async Task<HttpResponseMessage> EntrarPeloGoogleAsync(string email)
    {
        var idToken = _google.Emitir(email);
        return await _factory!.CreateClient().PostAsJsonAsync("/api/auth/google/login", new { idToken });
    }

    /// <summary>Contexto avulso para semear e conferir, fora de uma requisição (o banco do teste é superuser, sem RLS).</summary>
    private EasyStockDbContext Banco() =>
        new(new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(_pg!.GetConnectionString()).Options);

    private HttpClient Cliente(Guid empresaId, Guid usuarioId, NivelAcesso nivel)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256);
        // #1352: a validação de sessão exige `sub` de usuário real e ativo e `iat`.
        var claims = new List<Claim>
        {
            new("sub", usuarioId.ToString()),
            new("nivel", nivel.ToString()),
            new("empresaId", empresaId.ToString()),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        var token = new JwtSecurityToken(JwtIssuer, JwtAudience, claims, expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: creds);
        var http = _factory!.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return http;
    }

    private static async Task DeveSer(HttpResponseMessage resposta, int status, string? codigo = null)
    {
        var corpo = await resposta.Content.ReadAsStringAsync();
        ((int)resposta.StatusCode).Should().Be(status, $"{resposta.RequestMessage?.Method} {resposta.RequestMessage?.RequestUri}: {corpo}");
        if (codigo is not null)
            JsonDocument.Parse(corpo).RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(codigo);
    }

    private static async Task<JsonElement> DadosAsync(HttpResponseMessage resposta)
    {
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, corpo);
        return JsonDocument.Parse(corpo).RootElement.GetProperty("data").Clone();
    }

    private static Dictionary<string, string> Claims(string jwt) =>
        new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims
            .GroupBy(c => c.Type).ToDictionary(g => g.Key, g => g.First().Value);

    /// <summary>Google falso: o "ID token" é um identificador que aponta para um e-mail verificado.</summary>
    private sealed class GoogleFalso : IGoogleIdTokenValidator
    {
        private readonly Dictionary<string, string> _emails = new();

        public string? ClientId => "cliente-google-de-teste";
        public string? EmpresaPadrao => null;

        public string Emitir(string email)
        {
            var token = $"id-token-{Guid.NewGuid():N}";
            lock (_emails) _emails[token] = email;
            return token;
        }

        public Task<IdentidadeGoogle?> ValidarAsync(string idToken, CancellationToken ct = default)
        {
            lock (_emails)
                return Task.FromResult(_emails.TryGetValue(idToken, out var email) ? new IdentidadeGoogle(email, true) : null);
        }
    }
}
