using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Services;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// #2016 (antes #1575/#1588): os dois jobs de estoque rodam POR EMPRESA, cada uma num escopo com o tenant ligado, e fazem
/// o trabalho. Antes, fora de request o tenant era <c>Guid.Empty</c>, o filtro global do DbContext e a RLS zeravam toda
/// entidade com empresa e os jobs percorriam as empresas sem fazer nada. DI de produção, banco real; com
/// <c>rls</c>, o login é sujeito à RLS como o da aplicação (nenhum bypass).
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class AlertasDeEstoquePorEmpresaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private sealed record Cenario(
        Empresa Empresa, Loja Loja, Produto Critico, ItemEstoque AVencer, ItemEstoque Vencido, ItemEstoque VencidoZerado);

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Analisador_AvisaCadaEmpresaDoQueEDela_MarcaOVencido_ENaoRepeteNaRodadaSeguinte(bool rls)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var a = await SemearAsync("Alertas A");
        var b = await SemearAsync("Alertas B");
        await using var provider = BuildProvider(rls);
        var analisador = new AnalisadorEstoqueBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AnalisadorEstoqueBackgroundService>.Instance);

        (await analisador.RodadaAsync(CancellationToken.None)).Should().BeTrue("ninguém detém a trava");

        foreach (var (dona, outra) in new[] { (a, b), (b, a) })
        {
            var avisos = await NotificacoesAsync(dona.Empresa.Id);
            avisos.Should().OnlyContain(n => n.EmpresaId == dona.Empresa.Id && n.UsuarioId == null,
                "o aviso é da empresa e vai para toda a equipe, sem destinatário");
            avisos.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.ValidadeProxima && n.ReferenciaId == dona.AVencer.Id);
            avisos.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.ProdutoVencido && n.ReferenciaId == dona.Vencido.Id);
            avisos.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.EstoqueCritico && n.ReferenciaId == dona.Critico.Id);
            avisos.Should().Contain(n => n.TipoAlerta == TipoAlertaEstoque.ReposicaoSugerida && n.ReferenciaId == dona.Critico.Id);
            avisos.Should().NotContain(n => n.ReferenciaId == dona.VencidoZerado.Id, "lote zerado não vai para a prateleira nem para o sino");
            avisos.Select(n => n.ReferenciaId).Should().NotIntersectWith(
                new Guid?[] { outra.AVencer.Id, outra.Vencido.Id, outra.Critico.Id }, "uma empresa não recebe o aviso da outra");

            (await StatusAsync(dona.Empresa.Id, dona.Vencido.Id)).Should().Be(StatusItemEstoque.Vencido,
                "o lote que venceu parado é marcado na rodada (estoque-08)");
            (await StatusAsync(dona.Empresa.Id, dona.VencidoZerado.Id)).Should().NotBe(StatusItemEstoque.Vencido, "sem saldo, ninguém mexe");
        }

        var totalNaPrimeira = (await NotificacoesAsync(a.Empresa.Id)).Count + (await NotificacoesAsync(b.Empresa.Id)).Count;
        totalNaPrimeira.Should().BeGreaterThanOrEqualTo(8);

        (await analisador.RodadaAsync(CancellationToken.None)).Should().BeTrue();

        ((await NotificacoesAsync(a.Empresa.Id)).Count + (await NotificacoesAsync(b.Empresa.Id)).Count)
            .Should().Be(totalNaPrimeira, "no mesmo dia o alerta de cada item sai uma vez só, rodada após rodada");
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecalcularVelocidades_GravaAVelocidadeDeCadaEmpresa_SoDoQueMudou(bool rls)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var agora = DateTime.UtcNow;
        var a = await SemearVelocidadeAsync("Velocidades A", saidas: 60, agora); // 2 por dia em 30 dias
        var b = await SemearVelocidadeAsync("Velocidades B", saidas: 30, agora); // 1 por dia
        await using var provider = BuildProvider(rls);
        var job = new RecalcularVelocidadesJob(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<RecalcularVelocidadesJob>.Instance);

        (await job.RodadaAsync(CancellationToken.None)).Should().BeTrue("ninguém detém a trava");

        (await LoteAsync(a.Empresa.Id, a.Lote.Id)).VelocidadeSaidaDiaria.Should().Be(2m);
        (await LoteAsync(b.Empresa.Id, b.Lote.Id)).VelocidadeSaidaDiaria.Should().Be(1m, "cada empresa recebe a velocidade dos próprios produtos");
        (await LoteAsync(a.Empresa.Id, a.Zerado.Id)).VelocidadeSaidaDiaria.Should().Be(0m, "lote sem saldo não é mexido");

        var alteradoEm = (await LoteAsync(a.Empresa.Id, a.Lote.Id)).AlteradoEm;
        (await job.RodadaAsync(CancellationToken.None)).Should().BeTrue();

        var depois = await LoteAsync(a.Empresa.Id, a.Lote.Id);
        depois.VelocidadeSaidaDiaria.Should().Be(2m);
        depois.AlteradoEm.Should().Be(alteradoEm, "velocidade igual não grava de novo");
    }

    private async Task<Cenario> SemearAsync(string nome)
    {
        var agora = DateTime.UtcNow;
        var hoje = agora.Date;
        var empresa = Empresa.Criar(nome, null);
        var loja = new Loja { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Loja", Ativa = true, CriadoEm = agora, AlteradoEm = agora };
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora };
        // Mínimo 5 e crítico 2: com 1 unidade na prateleira o produto está crítico.
        var critico = NovoProduto(empresa, categoria, "Lasanha", agora, minima: 5, critica: 2);
        var comum = NovoProduto(empresa, categoria, "Nhoque", agora);
        var aVencer = NovoLote(empresa, loja, critico, saldo: 1, validade: hoje.AddDays(2), agora);
        var vencido = NovoLote(empresa, loja, comum, saldo: 10, validade: hoje.AddDays(-3), agora);
        var vencidoZerado = NovoLote(empresa, loja, comum, saldo: 0, validade: hoje.AddDays(-30), agora);

        await using var seed = fixture.CreateDbContext();
        seed.SetMobileTenantContext(empresa.Id);
        seed.Empresas.Add(empresa);
        seed.Lojas.Add(loja);
        seed.Categorias.Add(categoria);
        seed.Produtos.AddRange(critico, comum);
        seed.ItensEstoque.AddRange(aVencer, vencido, vencidoZerado);
        await seed.SaveChangesAsync();
        return new Cenario(empresa, loja, critico, aVencer, vencido, vencidoZerado);
    }

    private sealed record CenarioVelocidade(Empresa Empresa, ItemEstoque Lote, ItemEstoque Zerado);

    private async Task<CenarioVelocidade> SemearVelocidadeAsync(string nome, int saidas, DateTime agora)
    {
        var empresa = Empresa.Criar(nome, null);
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora };
        var produto = NovoProduto(empresa, categoria, "Lasanha", agora);
        var lote = NovoLote(empresa, null, produto, saldo: 40, validade: null, agora);
        var zerado = NovoLote(empresa, null, produto, saldo: 0, validade: null, agora);
        var saida = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, ItemEstoqueId = lote.Id, ProdutoId = produto.Id,
            Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.Venda, Quantidade = Quantidade.From(saidas),
            DataMovimentacao = agora.AddDays(-5), CriadoEm = agora,
        };

        await using var seed = fixture.CreateDbContext();
        seed.SetMobileTenantContext(empresa.Id);
        seed.Empresas.Add(empresa);
        seed.Categorias.Add(categoria);
        seed.Produtos.Add(produto);
        seed.ItensEstoque.AddRange(lote, zerado);
        seed.MovimentacoesEstoque.Add(saida);
        await seed.SaveChangesAsync();
        return new CenarioVelocidade(empresa, lote, zerado);
    }

    private static Produto NovoProduto(Empresa empresa, Categoria categoria, string nome, DateTime agora, int? minima = null, int? critica = null) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresa.Id, CategoriaId = categoria.Id, Nome = nome,
        Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, QuantidadeMinima = minima, QuantidadeCritica = critica,
        CriadoEm = agora, AlteradoEm = agora,
    };

    private static ItemEstoque NovoLote(Empresa empresa, Loja? loja, Produto produto, decimal saldo, DateTime? validade, DateTime agora) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresa.Id, LojaId = loja?.Id, ProdutoId = produto.Id,
        QuantidadeInicial = Quantidade.From(100), QuantidadeAtual = Quantidade.From(saldo),
        CustoUnitario = Dinheiro.FromDecimal(10m), EntradaEm = agora.AddDays(-40),
        ValidadeEm = validade.HasValue ? Validade.From(DateTime.SpecifyKind(validade.Value, DateTimeKind.Utc)) : null,
        Status = StatusItemEstoque.Ok, CriadoEm = agora, AlteradoEm = agora,
    };

    private async Task<List<Notificacao>> NotificacoesAsync(Guid empresaId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return await db.Notificacoes.AsNoTracking().Where(n => n.EmpresaId == empresaId).ToListAsync();
    }

    private async Task<ItemEstoque> LoteAsync(Guid empresaId, Guid loteId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return await db.ItensEstoque.AsNoTracking().SingleAsync(i => i.Id == loteId);
    }

    private async Task<StatusItemEstoque> StatusAsync(Guid empresaId, Guid loteId) => (await LoteAsync(empresaId, loteId)).Status;

    /// <summary>DI de produção. Com <paramref name="rls"/>, o login do banco é sujeito a RLS, como o da aplicação.</summary>
    private ServiceProvider BuildProvider(bool rls)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>()); // sem JWT, como no job
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(rls ? fixture.RlsClientConnectionString : fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        services.AddScoped<GeradorNotificacoesAutomaticas>();
        return services.BuildServiceProvider();
    }
}
