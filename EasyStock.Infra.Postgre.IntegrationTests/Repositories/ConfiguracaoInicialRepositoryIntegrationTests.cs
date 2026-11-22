using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Queries;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2070: as marcas dos passos da configuração inicial saem de um upsert atômico. Dois aparelhos marcando passos
/// diferentes gravam os dois; o mesmo passo ao mesmo tempo fica numa linha só; o RLS isola a empresa. Tudo medido com o
/// papel da API (NOSUPERUSER, NOBYPASSRLS), não com o superusuário que ignora as policies.
/// </summary>
public class ConfiguracaoInicialRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Antes = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Depois = Antes.AddMinutes(3);

    private async Task<Guid> NovaEmpresaAsync()
    {
        var empresa = Empresa.Criar("Casa da Baba Primeiros Passos", Guid.NewGuid().ToString("N")[..14]);
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        return empresa.Id;
    }

    private async Task<Usuario> NovoUsuarioAsync(string nome)
    {
        var usuario = Usuario.Criar(nome, $"passos-{Guid.NewGuid():N}@exemplo.test", "sem-login-por-senha");
        await using var db = fixture.CreateDbContext();
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    /// <summary>Contexto como a API: papel sujeito ao RLS, interceptor e tenant fixado.</summary>
    private EasyStockDbContext NovoDbComRls(Guid empresaId)
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        var db = new EasyStockDbContext(options);
        db.SetMobileTenantContext(empresaId);
        return db;
    }

    private static PassoConfiguracaoInicial Marca(Guid empresaId, string passo, SituacaoDoPasso situacao, Guid autor, DateTime em) =>
        PassoConfiguracaoInicial.Marcar(empresaId, passo, situacao, autor, em);

    private async Task<List<PassoConfiguracaoInicial>> LinhasAsync(Guid empresaId)
    {
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        return await db.ConfiguracaoInicialPassos.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId).OrderBy(p => p.Passo).ToListAsync();
    }

    [SkippableFact]
    public async Task DoisAparelhosMarcandoPassosDiferentesAoMesmoTempoGravamOsDois()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = await NovaEmpresaAsync();
        var dona = await NovoUsuarioAsync("Dona de teste");

        await using var celular = NovoDbComRls(empresaId);
        await using var computador = NovoDbComRls(empresaId);
        await using var txCelular = await celular.Database.BeginTransactionAsync();
        await using var txComputador = await computador.Database.BeginTransactionAsync();
        await new ConfiguracaoInicialRepository(celular).MarcarAsync(Marca(empresaId, "horarios", SituacaoDoPasso.Conferido, dona.Id, Antes));

        // Com a transação do celular aberta, o upsert de outro passo não espera por ela.
        await new ConfiguracaoInicialRepository(computador)
            .MarcarAsync(Marca(empresaId, "frete", SituacaoDoPasso.Depois, dona.Id, Depois))
            .WaitAsync(TimeSpan.FromSeconds(15));
        await txComputador.CommitAsync();
        await txCelular.CommitAsync();

        var linhas = await LinhasAsync(empresaId);
        linhas.Select(l => (l.Passo, l.Situacao, l.Em, l.PorUsuarioId)).Should().Equal(
            ("frete", SituacaoDoPasso.Depois, Depois, dona.Id),
            ("horarios", SituacaoDoPasso.Conferido, Antes, dona.Id));
    }

    [SkippableFact]
    public async Task MesmoPassoAoMesmoTempoFicaNumaLinhaComAUltimaMarca()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = await NovaEmpresaAsync();
        var dona = await NovoUsuarioAsync("Dona de teste");
        var gerente = await NovoUsuarioAsync("Administração de teste");

        await using var primeiro = NovoDbComRls(empresaId);
        await using var segundo = NovoDbComRls(empresaId);
        await using var txPrimeiro = await primeiro.Database.BeginTransactionAsync();
        await using var txSegundo = await segundo.Database.BeginTransactionAsync();
        await new ConfiguracaoInicialRepository(primeiro).MarcarAsync(Marca(empresaId, "frete", SituacaoDoPasso.Conferido, dona.Id, Antes));

        // O segundo upsert do mesmo passo fica preso na linha do primeiro até ele confirmar; aí vira UPDATE, sem 23505.
        var marcaDoSegundo = new ConfiguracaoInicialRepository(segundo)
            .MarcarAsync(Marca(empresaId, "frete", SituacaoDoPasso.Depois, gerente.Id, Depois));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        marcaDoSegundo.IsCompleted.Should().BeFalse("o upsert do mesmo passo espera a transação que tem a linha");
        await txPrimeiro.CommitAsync();
        await marcaDoSegundo.WaitAsync(TimeSpan.FromSeconds(15));
        await txSegundo.CommitAsync();

        var linhas = await LinhasAsync(empresaId);
        linhas.Select(l => (l.Passo, l.Situacao, l.Em, l.PorUsuarioId))
            .Should().Equal(("frete", SituacaoDoPasso.Depois, Depois, gerente.Id));
    }

    [SkippableFact]
    public async Task VinteMarcasSimultaneasDoMesmoPassoNaoDuplicam()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = await NovaEmpresaAsync();
        var dona = await NovoUsuarioAsync("Dona de teste");

        await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            await using var db = NovoDbComRls(empresaId);
            await new ConfiguracaoInicialRepository(db)
                .MarcarAsync(Marca(empresaId, "cardapio", SituacaoDoPasso.Conferido, dona.Id, Antes.AddSeconds(i)));
        }));

        (await LinhasAsync(empresaId)).Should().ContainSingle().Which.Passo.Should().Be("cardapio");
    }

    [SkippableFact]
    public async Task ListarTrazOsPassosDaEmpresaComONomeDeQuemMarcou()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = await NovaEmpresaAsync();
        var dona = await NovoUsuarioAsync("Dona de teste");
        await using (var db = NovoDbComRls(empresaId))
        {
            var repositorio = new ConfiguracaoInicialRepository(db);
            await repositorio.MarcarAsync(Marca(empresaId, "janelas", SituacaoDoPasso.Conferido, dona.Id, Antes));
            await repositorio.MarcarAsync(Marca(empresaId, "equipe", SituacaoDoPasso.Depois, dona.Id, Depois));
        }

        await using var leitura = NovoDbComRls(empresaId);
        var passos = await new ConfiguracaoInicialRepository(leitura).ListarPassosAsync(empresaId);

        passos.Should().Equal(
            new EasyStock.Application.Ports.Output.Persistence.PassoMarcado("equipe", SituacaoDoPasso.Depois, Depois, dona.Id, "Dona de teste"),
            new EasyStock.Application.Ports.Output.Persistence.PassoMarcado("janelas", SituacaoDoPasso.Conferido, Antes, dona.Id, "Dona de teste"));
    }

    [SkippableFact]
    public async Task OutraEmpresaNaoLeNemMarcaOsPassosDaDona()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var dona = await NovaEmpresaAsync();
        var intrusa = await NovaEmpresaAsync();
        var autor = await NovoUsuarioAsync("Dona de teste");
        await using (var db = NovoDbComRls(dona))
            await new ConfiguracaoInicialRepository(db).MarcarAsync(Marca(dona, "horarios", SituacaoDoPasso.Conferido, autor.Id, Antes));

        await using (var db = NovoDbComRls(intrusa))
        {
            (await new ConfiguracaoInicialRepository(db).ListarPassosAsync(dona)).Should().BeEmpty();
            (await db.ConfiguracaoInicialPassos.IgnoreQueryFilters().CountAsync(p => p.EmpresaId == dona)).Should().Be(0);
            var falha = await FluentActions
                .Awaiting(() => new ConfiguracaoInicialRepository(db).MarcarAsync(Marca(dona, "frete", SituacaoDoPasso.Depois, autor.Id, Depois)))
                .Should().ThrowAsync<PostgresException>();
            falha.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        (await LinhasAsync(dona)).Select(l => l.Passo).Should().Equal("horarios");
    }

    // ── Retrato da prontidão ─────────────────────────────────────────────────

    [SkippableFact]
    public async Task RetratoDeEmpresaVaziaZeraTudo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = await NovaEmpresaAsync();

        await using var db = NovoDbComRls(empresaId);
        var retrato = await new RetratoDaConfiguracaoInicialQuery(db).ObterAsync(empresaId);

        retrato.Should().BeEquivalentTo(new EasyStock.Domain.Services.RetratoDaConfiguracaoInicial(
            false, 0, false, false, 0, 0, 0, false, new Dictionary<NivelAcesso, int>()));
    }

    [SkippableFact]
    public async Task RetratoContaOQueALojaJaTemEOutraEmpresaNaoVe()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = await NovaEmpresaAsync();
        var intrusa = await NovaEmpresaAsync();
        await SemearLojaAsync(empresaId);

        await using (var db = NovoDbComRls(empresaId))
        {
            var retrato = await new RetratoDaConfiguracaoInicialQuery(db).ObterAsync(empresaId);

            retrato.Should().BeEquivalentTo(new EasyStock.Domain.Services.RetratoDaConfiguracaoInicial(
                HorariosGravados: true, DiasAbertos: 7, TemVitrine: true, TemLogo: true,
                // Avulso com preço e vinculado ao produto com preço contam; oculto, arquivado e produto sem preço, não.
                ItensVisiveisComPreco: 2, JanelasAtivas: 1, ZonasAtivas: 1, RaioConfigurado: false,
                EquipeAtivaPorNivel: new Dictionary<NivelAcesso, int> { [NivelAcesso.Admin] = 1, [NivelAcesso.Operador] = 1 }));
        }

        await using (var db = NovoDbComRls(intrusa))
        {
            var retrato = await new RetratoDaConfiguracaoInicialQuery(db).ObterAsync(empresaId);
            retrato.Should().BeEquivalentTo(new EasyStock.Domain.Services.RetratoDaConfiguracaoInicial(
                false, 0, false, false, 0, 0, 0, false, new Dictionary<NivelAcesso, int>()), "o RLS esconde a loja de outra empresa");
        }
    }

    private async Task SemearLojaAsync(Guid empresaId)
    {
        var agora = DateTime.UtcNow;
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);

        db.ExpedientesLoja.Add(ExpedienteLoja.CriarPadrao(empresaId));

        var vitrine = StorefrontEntity.Criar(empresaId, $"passos-{Guid.NewGuid():N}"[..30], "Vitrine", 0m);
        vitrine.AtualizarBranding(logoUrl: "https://exemplo.test/logo.png");
        db.Storefronts.Add(vitrine);

        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora };
        db.Set<Categoria>().Add(categoria);
        var comPreco = NovoProduto(empresaId, categoria.Id, "Raviol", 30m, agora);
        var semPreco = NovoProduto(empresaId, categoria.Id, "Nhoque", null, agora);
        db.Set<Produto>().AddRange(comPreco, semPreco);

        var avulso = CardapioItem.CriarAvulso(vitrine.Id, "Lasanha", 42m);
        avulso.TornarVisivel();
        var vinculado = CardapioItem.CriarAPartirDeProduto(vitrine.Id, comPreco);
        vinculado.TornarVisivel();
        var vinculadoSemPreco = CardapioItem.CriarAPartirDeProduto(vitrine.Id, semPreco);
        vinculadoSemPreco.TornarVisivel();
        var oculto = CardapioItem.CriarAvulso(vitrine.Id, "Canelone", 38m);
        var arquivado = CardapioItem.CriarAvulso(vitrine.Id, "Rondelli", 35m);
        arquivado.TornarVisivel();
        arquivado.Arquivar(agora);
        db.CardapioItens.AddRange(avulso, vinculado, vinculadoSemPreco, oculto, arquivado);

        db.JanelasEntrega.Add(JanelaEntrega.Criar(vitrine.Id, 2, new TimeOnly(10, 0), new TimeOnly(12, 0), 10, "Manhã"));
        var janelaInativa = JanelaEntrega.Criar(vitrine.Id, 3, new TimeOnly(10, 0), new TimeOnly(12, 0), 10, "Manhã de quarta");
        janelaInativa.Desativar();
        db.JanelasEntrega.Add(janelaInativa);
        db.FreteZonas.Add(FreteZona.CriarPorBairros(vitrine.Id, "Butantã", ["butanta"], 8m, 40));

        await SemearPessoaAsync(db, empresaId, NivelAcesso.Admin, vinculoAtivo: true, agora);
        await SemearPessoaAsync(db, empresaId, NivelAcesso.Operador, vinculoAtivo: true, agora);
        await SemearPessoaAsync(db, empresaId, NivelAcesso.Operador, vinculoAtivo: false, agora);

        await db.SaveChangesAsync();
    }

    private static Produto NovoProduto(Guid empresaId, Guid categoriaId, string nome, decimal? preco, DateTime agora) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoriaId, Nome = nome, Status = StatusProduto.Ativo,
        PrecoReferencia = preco is { } p ? Dinheiro.FromDecimal(p) : null, CriadoEm = agora, AlteradoEm = agora
    };

    private static Task SemearPessoaAsync(EasyStockDbContext db, Guid empresaId, NivelAcesso nivel, bool vinculoAtivo, DateTime agora)
    {
        var usuario = Usuario.Criar($"Equipe {nivel}", $"equipe-{Guid.NewGuid():N}@exemplo.test", "sem-login-por-senha");
        var perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = $"Perfil {nivel} {Guid.NewGuid():N}"[..30], Nivel = nivel, CriadoEm = agora };
        db.Usuarios.Add(usuario);
        db.Perfis.Add(perfil);
        db.UsuariosEmpresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresaId, Ativo = vinculoAtivo, CriadoEm = agora });
        db.UsuariosPerfis.Add(new UsuarioPerfil { Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresaId, PerfilId = perfil.Id, AtribuidoEm = agora });
        return Task.CompletedTask;
    }
}
