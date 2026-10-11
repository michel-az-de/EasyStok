using System.Reflection;
using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Services;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// #1575, DOCUMENTAÇÃO DO COMPORTAMENTO ATUAL (não é o comportamento desejado): o analisador de estoque
/// (<see cref="GeradorNotificacoesAutomaticas"/>, de hora em hora) e o <see cref="RecalcularVelocidadesJob"/>
/// (a cada 6 h) rodam fora de request, sem tenant no contexto e sem bypass. O filtro global do DbContext
/// (<c>EmpresaId == CurrentTenantId</c>, que fora de request é <c>Guid.Empty</c>) devolve zero linha de toda
/// entidade com empresa, com ou sem RLS no banco: os dois jobs percorrem as empresas e não fazem nada.
///
/// <para>
/// Cada prova tem o controle ao lado: as mesmas consultas, com o tenant da empresa no contexto, enxergam o
/// dado. Dar tenant aos jobs é decisão de produto (notificação diária sem retenção, uma consulta por item);
/// quando isso mudar, estes testes mudam junto.
/// </para>
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class JobsDeEstoqueSemTenantIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnalisadorDeEstoque_ForaDeRequest_NaoCriaNotificacao(bool rls)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Analisador sem tenant", null);
        var fornecedor = new Fornecedor { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Fornecedor" };
        var atrasado = new PedidoFornecedor
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, FornecedorId = fornecedor.Id, DataPedido = agora.AddDays(-10),
            PrevisaoEntrega = agora.AddDays(-3), Status = StatusPedidoFornecedor.Aberto, CriadoEm = agora, AlteradoEm = agora,
        };
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa.Id);
            seed.Empresas.Add(empresa);
            seed.Lojas.Add(new Loja { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Loja", Ativa = true, CriadoEm = agora, AlteradoEm = agora });
            seed.Set<Fornecedor>().Add(fornecedor);
            seed.PedidosFornecedor.Add(atrasado);
            await seed.SaveChangesAsync();
        }
        await using var provider = BuildProvider(rls);

        // Como o AnalisadorEstoqueBackgroundService roda: escopo novo, sem usuário e sem tenant.
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<GeradorNotificacoesAutomaticas>().ExecutarAsync();

        (await NotificacoesAsync(empresa.Id)).Should().BeEmpty("fora de request o filtro global esconde o pedido atrasado da empresa");

        // Controle: o mesmo gerador, com o tenant da empresa no contexto, avisa do pedido atrasado.
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresa.Id);
            await scope.ServiceProvider.GetRequiredService<GeradorNotificacoesAutomaticas>().ExecutarAsync();
        }

        (await NotificacoesAsync(empresa.Id)).Should().ContainSingle(n =>
            n.TipoAlerta == TipoAlertaEstoque.PedidoAtrasado && n.ReferenciaId == atrasado.Id);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecalcularVelocidades_ForaDeRequest_NaoAtualizaNenhumLote(bool rls)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Velocidades sem tenant", null);
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora };
        var produto = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, CategoriaId = categoria.Id, Nome = "Lasanha",
            Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = agora, AlteradoEm = agora,
        };
        var lote = new ItemEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, ProdutoId = produto.Id,
            QuantidadeInicial = Quantidade.From(100), QuantidadeAtual = Quantidade.From(40),
            CustoUnitario = Dinheiro.FromDecimal(10m), EntradaEm = agora.AddDays(-40),
            Status = StatusItemEstoque.Ok, CriadoEm = agora, AlteradoEm = agora,
        };
        // 60 saídas em 30 dias: o job gravaria 2 por dia.
        var saida = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresa.Id, ItemEstoqueId = lote.Id, ProdutoId = produto.Id,
            Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.Venda, Quantidade = Quantidade.From(60),
            DataMovimentacao = agora.AddDays(-5), CriadoEm = agora,
        };
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresa.Id);
            seed.Empresas.Add(empresa);
            seed.Categorias.Add(categoria);
            seed.Produtos.Add(produto);
            seed.ItensEstoque.Add(lote);
            seed.MovimentacoesEstoque.Add(saida);
            await seed.SaveChangesAsync();
        }
        await using var provider = BuildProvider(rls);

        // A rodada do job como ele a executa (escopo próprio, sem tenant). O método é privado e o laço
        // público espera 6 h; a reflexão quebra com mensagem clara se o nome mudar.
        var job = new RecalcularVelocidadesJob(provider, NullLogger<RecalcularVelocidadesJob>.Instance);
        var rodada = typeof(RecalcularVelocidadesJob).GetMethod("RecalcularVelocidadesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        rodada.Should().NotBeNull("este teste chama a rodada do job por reflexão");
        await (Task)rodada!.Invoke(job, [CancellationToken.None])!;

        await using (var leitura = fixture.CreateDbContext())
        {
            leitura.SetMobileTenantContext(empresa.Id);
            (await leitura.ItensEstoque.AsNoTracking().SingleAsync(i => i.Id == lote.Id)).VelocidadeSaidaDiaria
                .Should().Be(0m, "fora de request o job não enxerga o lote e não grava a velocidade");
        }

        // Controle: as consultas do job enxergam o lote e a taxa só com o tenant no contexto.
        await using (var semTenant = provider.CreateAsyncScope())
        {
            var (itens, _) = await semTenant.ServiceProvider.GetRequiredService<IItemEstoqueRepository>()
                .GetItensEstoquePaginadosAsync(empresa.Id, 1, 500);
            itens.Should().BeEmpty();
        }
        await using (var comTenant = provider.CreateAsyncScope())
        {
            comTenant.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresa.Id);
            var (itens, _) = await comTenant.ServiceProvider.GetRequiredService<IItemEstoqueRepository>()
                .GetItensEstoquePaginadosAsync(empresa.Id, 1, 500);
            itens.Should().ContainSingle(i => i.Id == lote.Id);
            var taxas = await comTenant.ServiceProvider.GetRequiredService<IMovimentacaoEstoqueRepository>()
                .GetTaxaSaidaDiariaPorProdutoAsync(empresa.Id, [produto.Id], agora.AddDays(-30), agora);
            taxas[produto.Id].Should().Be(2m);
        }
    }

    private async Task<List<Notificacao>> NotificacoesAsync(Guid empresaId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return await db.Notificacoes.AsNoTracking().Where(n => n.EmpresaId == empresaId).ToListAsync();
    }

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
