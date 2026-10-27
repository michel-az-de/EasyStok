using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests.ConfiguracaoInicial;

/// <summary>API real e Postgres real, um container para a classe (#2070).</summary>
public sealed class ConfiguracaoInicialApiFixture : IAsyncLifetime
{
    public const string JwtIssuer = "EasyStock";
    public const string JwtAudience = "EasyStock";
    public const string JwtSecret = "EasyStock-Test-SuperSecretKey-Min32Chars!!";

    private PostgreSqlContainer? _pg;
    public bool Disponivel { get; private set; }
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_configuracao_inicial")
                .WithUsername("postgres")
                .WithPassword("configuracao-inicial-senha") // #1890: StartupHardening recusa postgres/postgres
                .Build();
            await _pg.StartAsync();
        }
        catch (DockerUnavailableException)
        {
            return;
        }

        var conexao = _pg.GetConnectionString();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            // Conexão e JWT por UseSetting: chegam antes de o Program.cs ler a configuração (#1890).
            b.UseSetting("Database:Provider", "PostgreSql");
            b.UseSetting("ConnectionStrings:DefaultConnection", conexao);
            b.UseSetting("Jwt:SecretKey", JwtSecret);
            b.UseSetting("Jwt:Issuer", JwtIssuer);
            b.UseSetting("Jwt:Audience", JwtAudience);
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql",
                ["ConnectionStrings:DefaultConnection"] = conexao,
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["Jwt:SecretKey"] = JwtSecret,
                ["Jwt:ExpirationMinutes"] = "60",
                ["Anthropic:Enabled"] = "false",
                ["FileStorage:Provider"] = "Local",
                ["MercadoPago:UseStub"] = "true",
                ["BackgroundJobs:EnableVigiaIntegracoes"] = "false",
            }));
        });
        Factory.CreateClient().Dispose(); // sobe a API e roda as migrações uma vez
        Disponivel = true;
    }

    public async Task DisposeAsync()
    {
        if (Disponivel) await Factory.DisposeAsync();
        if (_pg is not null) await _pg.DisposeAsync();
    }
}

/// <summary>
/// #2070 ponta a ponta: contrato do <c>GET api/configuracao-inicial</c>, 400 para passo desconhecido, dois aparelhos
/// marcando passos diferentes ao mesmo tempo, 403 HIERARQUIA para a Administração no adiar e no concluir, e a chave
/// <c>acoes.configurarLoja</c> em <c>me/modulos</c>.
/// </summary>
public sealed class ConfiguracaoInicialE2ETests(ConfiguracaoInicialApiFixture fx) : IClassFixture<ConfiguracaoInicialApiFixture>
{
    private const string Rota = "/api/configuracao-inicial";

    private sealed record Loja(Guid EmpresaId, Guid DonaId, Guid AdministracaoId, Guid SuporteId);

    private async Task<Loja> SemearLojaAsync()
    {
        var agora = DateTime.UtcNow;
        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();

        var empresa = Empresa.Criar("Casa da Baba Primeiros Passos", Guid.NewGuid().ToString("N")[..14]);
        db.Empresas.Add(empresa);
        var perfilAdmin = new Perfil
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Administração", Nivel = NivelAcesso.Admin, CriadoEm = agora
        };
        var perfilSuporte = new Perfil
        {
            Id = Guid.NewGuid(), EmpresaId = null, Nome = $"Suporte {Guid.NewGuid():N}"[..30], Nivel = NivelAcesso.SuperAdmin, CriadoEm = agora
        };
        db.Perfis.AddRange(perfilAdmin, perfilSuporte);

        var dona = Pessoa(db, empresa.Id, perfilAdmin.Id, "Dona de teste", dona: true, agora);
        var administracao = Pessoa(db, empresa.Id, perfilAdmin.Id, "Administração de teste", dona: false, agora);
        var suporte = Usuario.Criar("Suporte de teste", $"suporte-{Guid.NewGuid():N}@exemplo.test", "sem-login-por-senha");
        db.Usuarios.Add(suporte);
        db.UsuariosPerfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = suporte.Id, EmpresaId = empresa.Id, PerfilId = perfilSuporte.Id, AtribuidoEm = agora
        });

        await db.SaveChangesAsync();
        return new Loja(empresa.Id, dona, administracao, suporte.Id);
    }

    private static Guid Pessoa(EasyStockDbContext db, Guid empresaId, Guid perfilId, string nome, bool dona, DateTime agora)
    {
        var usuario = Usuario.Criar(nome, $"pessoa-{Guid.NewGuid():N}@exemplo.test", "sem-login-por-senha");
        db.Usuarios.Add(usuario);
        db.UsuariosEmpresas.Add(new UsuarioEmpresa
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresaId, Ativo = true, EhDona = dona, CriadoEm = agora
        });
        db.UsuariosPerfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresaId, PerfilId = perfilId, AtribuidoEm = agora
        });
        return usuario.Id;
    }

    private HttpClient Cliente(Guid usuarioId, Guid? empresaId, NivelAcesso nivel = NivelAcesso.Admin)
    {
        var claims = new List<Claim>
        {
            new("sub", usuarioId.ToString()),
            new("nivel", nivel.ToString()),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        if (empresaId is { } empresa) claims.Add(new Claim("empresaId", empresa.ToString()));
        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ConfiguracaoInicialApiFixture.JwtSecret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(ConfiguracaoInicialApiFixture.JwtIssuer, ConfiguracaoInicialApiFixture.JwtAudience, claims,
            expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: credenciais);

        var http = fx.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return http;
    }

    private static async Task<JsonElement> DadosAsync(HttpClient http)
    {
        var resposta = await http.GetAsync(Rota);
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, corpo);
        return JsonDocument.Parse(corpo).RootElement.GetProperty("data");
    }

    private static JsonElement PassoDe(JsonElement dados, string passo) =>
        dados.GetProperty("passos").EnumerateArray().Single(p => p.GetProperty("passo").GetString() == passo);

    private static async Task<string?> CodigoDoErroAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString();

    [SkippableFact]
    public async Task GetDevolveOContratoDaEspecificacaoParaADona()
    {
        Skip.IfNot(fx.Disponivel, "Docker indisponível");
        var loja = await SemearLojaAsync();

        var dados = await DadosAsync(Cliente(loja.DonaId, loja.EmpresaId));

        dados.EnumerateObject().Select(p => p.Name).Should().Equal(
            "pendente", "abrirSozinho", "ehDona", "podeConcluir", "concluidaEm", "adiadaEm", "passos");
        (dados.GetProperty("pendente").GetBoolean(), dados.GetProperty("abrirSozinho").GetBoolean(),
                dados.GetProperty("ehDona").GetBoolean(), dados.GetProperty("podeConcluir").GetBoolean())
            .Should().Be((true, true, true, true));
        dados.GetProperty("concluidaEm").ValueKind.Should().Be(JsonValueKind.Null);
        dados.GetProperty("adiadaEm").ValueKind.Should().Be(JsonValueKind.Null);

        var passos = dados.GetProperty("passos").EnumerateArray().ToList();
        passos.Select(p => p.GetProperty("passo").GetString()).Should().Equal(
            "boas-vindas", "horarios", "site-e-impressos", "cardapio", "janelas", "frete", "equipe",
            "dados-da-loja", "atendimento", "respostas", "agente", "producao", "estoque", "entregadores", "impressora", "avisos",
            "pronto");
        passos.Select(p => p.GetProperty("ordem").GetInt32()).Should().Equal(Enumerable.Range(1, 17));
        passos.Should().AllSatisfy(p => p.EnumerateObject().Select(c => c.Name).Should().Equal(
            "passo", "parte", "ordem", "situacao", "em", "porNome", "disponivel", "motivo", "prontidao"));

        var boasVindas = passos[0];
        boasVindas.GetProperty("parte").GetString().Should().Be("essencial");
        boasVindas.GetProperty("situacao").ValueKind.Should().Be(JsonValueKind.Null);
        boasVindas.GetProperty("disponivel").GetBoolean().Should().BeTrue();
        boasVindas.GetProperty("prontidao").ValueKind.Should().Be(JsonValueKind.Null);
        PassoDe(dados, "dados-da-loja").GetProperty("parte").GetString().Should().Be("ajustes");
        PassoDe(dados, "pronto").GetProperty("parte").GetString().Should().Be("revisao");

        var horarios = PassoDe(dados, "horarios").GetProperty("prontidao");
        horarios.GetProperty("ok").GetBoolean().Should().BeFalse();
        horarios.GetProperty("falta").EnumerateArray().Select(f => f.GetString()).Should().Equal("Salvar os horários da semana");
    }

    [SkippableFact]
    public async Task PassoDesconhecidoDa400EPassoConhecidoApareceNoGetComQuemMarcou()
    {
        Skip.IfNot(fx.Disponivel, "Docker indisponível");
        var loja = await SemearLojaAsync();
        var http = Cliente(loja.AdministracaoId, loja.EmpresaId);

        var desconhecido = await http.PutAsJsonAsync($"{Rota}/passos/caixa", new { situacao = "conferido" });
        desconhecido.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoDoErroAsync(desconhecido)).Should().Be("PASSO_DESCONHECIDO");

        var situacaoInvalida = await http.PutAsJsonAsync($"{Rota}/passos/frete", new { situacao = "pulado" });
        situacaoInvalida.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoDoErroAsync(situacaoInvalida)).Should().Be("SITUACAO_INVALIDA");

        var marcado = await http.PutAsJsonAsync($"{Rota}/passos/frete", new { situacao = "depois" });
        marcado.StatusCode.Should().Be(HttpStatusCode.NoContent, await marcado.Content.ReadAsStringAsync());

        var frete = PassoDe(await DadosAsync(http), "frete");
        frete.GetProperty("situacao").GetString().Should().Be("depois");
        frete.GetProperty("porNome").GetString().Should().Be("Administração de teste");
        frete.GetProperty("em").ValueKind.Should().Be(JsonValueKind.String);
    }

    [SkippableFact]
    public async Task DoisAparelhosMarcandoPassosDiferentesAoMesmoTempoGravamOsDois()
    {
        Skip.IfNot(fx.Disponivel, "Docker indisponível");
        var loja = await SemearLojaAsync();
        var celular = Cliente(loja.DonaId, loja.EmpresaId);
        var computador = Cliente(loja.AdministracaoId, loja.EmpresaId);

        var respostas = await Task.WhenAll(
            celular.PutAsJsonAsync($"{Rota}/passos/horarios", new { situacao = "conferido" }),
            computador.PutAsJsonAsync($"{Rota}/passos/cardapio", new { situacao = "depois" }));

        respostas.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.NoContent);
        var dados = await DadosAsync(celular);
        (PassoDe(dados, "horarios").GetProperty("situacao").GetString(), PassoDe(dados, "horarios").GetProperty("porNome").GetString())
            .Should().Be(("conferido", "Dona de teste"));
        (PassoDe(dados, "cardapio").GetProperty("situacao").GetString(), PassoDe(dados, "cardapio").GetProperty("porNome").GetString())
            .Should().Be(("depois", "Administração de teste"));
    }

    [SkippableFact]
    public async Task AdministracaoRecebe403HierarquiaNoAdiarENoConcluirSemMudarNada()
    {
        Skip.IfNot(fx.Disponivel, "Docker indisponível");
        var loja = await SemearLojaAsync();
        var http = Cliente(loja.AdministracaoId, loja.EmpresaId);

        foreach (var acao in new[] { "adiar", "concluir" })
        {
            var resposta = await http.PostAsync($"{Rota}/{acao}", null);
            resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, acao);
            (await CodigoDoErroAsync(resposta)).Should().Be("HIERARQUIA", acao);
        }

        var dados = await DadosAsync(http);
        (dados.GetProperty("pendente").GetBoolean(), dados.GetProperty("abrirSozinho").GetBoolean(),
                dados.GetProperty("ehDona").GetBoolean(), dados.GetProperty("podeConcluir").GetBoolean())
            .Should().Be((true, false, false, false));
        dados.GetProperty("adiadaEm").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [SkippableFact]
    public async Task DonaAdiaEDepoisConcluiComPassoParaDepois()
    {
        Skip.IfNot(fx.Disponivel, "Docker indisponível");
        var loja = await SemearLojaAsync();
        var http = Cliente(loja.DonaId, loja.EmpresaId);
        (await http.PutAsJsonAsync($"{Rota}/passos/equipe", new { situacao = "depois" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await http.PostAsync($"{Rota}/adiar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var adiada = await DadosAsync(http);
        adiada.GetProperty("abrirSozinho").GetBoolean().Should().BeFalse("depois de continuar depois não abre sozinho");
        adiada.GetProperty("adiadaEm").ValueKind.Should().Be(JsonValueKind.String);

        (await http.PostAsync($"{Rota}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var concluida = await DadosAsync(http);
        concluida.GetProperty("pendente").GetBoolean().Should().BeFalse();
        concluida.GetProperty("concluidaEm").ValueKind.Should().Be(JsonValueKind.String);

        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var _ = db.UseRowLevelSecurityBypass();
        (await db.Empresas.AsNoTracking().SingleAsync(e => e.Id == loja.EmpresaId)).ConfiguracaoInicialConcluidaPorId
            .Should().Be(loja.DonaId);
    }

    [SkippableFact]
    public async Task SuporteGlobalConcluiSemSerDona()
    {
        Skip.IfNot(fx.Disponivel, "Docker indisponível");
        var loja = await SemearLojaAsync();
        var http = Cliente(loja.SuporteId, loja.EmpresaId, NivelAcesso.SuperAdmin);

        var antes = await DadosAsync(http);
        (antes.GetProperty("ehDona").GetBoolean(), antes.GetProperty("podeConcluir").GetBoolean(), antes.GetProperty("abrirSozinho").GetBoolean())
            .Should().Be((false, true, false));

        (await http.PostAsync($"{Rota}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DadosAsync(http)).GetProperty("pendente").GetBoolean().Should().BeFalse();
    }

    [SkippableFact]
    public async Task TokenSemEmpresaNaoChegaAConfiguracaoInicial()
    {
        Skip.IfNot(fx.Disponivel, "Docker indisponível");
        var loja = await SemearLojaAsync();
        var http = Cliente(loja.DonaId, empresaId: null);

        // O filtro de módulo (ModulosConvention) nega antes do controller: sem empresa no token, nenhum módulo libera.
        (await http.GetAsync(Rota)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await http.PutAsJsonAsync($"{Rota}/passos/frete", new { situacao = "conferido" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task MeModulosGanhaConfigurarLojaSemPerderAsAcoesQueJaExistiam()
    {
        Skip.IfNot(fx.Disponivel, "Docker indisponível");
        var loja = await SemearLojaAsync();

        var admin = await AcoesAsync(Cliente(loja.AdministracaoId, loja.EmpresaId));
        var gerente = await AcoesAsync(Cliente(loja.AdministracaoId, loja.EmpresaId, NivelAcesso.Gerente));

        admin.GetProperty("configurarLoja").GetBoolean().Should().BeTrue();
        gerente.GetProperty("configurarLoja").GetBoolean().Should().BeFalse();
        admin.EnumerateObject().Select(p => p.Name).Should().Contain(
            new[] { "editarCardapio", "editarProducao", "editarAtendimento", "editarRespostas", "verIntegracoes", "ajustarEntrega" });
        (admin.GetProperty("editarCardapio").GetBoolean(), gerente.GetProperty("editarCardapio").GetBoolean()).Should().Be((true, true));
    }

    private static async Task<JsonElement> AcoesAsync(HttpClient http)
    {
        var resposta = await http.GetAsync("/api/auth/me/modulos");
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, corpo);
        return JsonDocument.Parse(corpo).RootElement.GetProperty("data").GetProperty("acoes");
    }
}
