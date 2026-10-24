using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

/// <summary>
/// PEDIDO-03 e PEDIDO-06 (banca 23/10): o ramo do checkout guest sem janela (ponte #1306) contra o EF real.
/// Os testes unitarios usam repositorio em memoria, que nao faz o fixup de <c>pedido.Itens</c>.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class CheckoutGuestSemJanelaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private const string Cep = "01310100";

    [SkippableFact]
    public async Task SemJanela_TotalSomaCadaItemUmaVez()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();
        var (empresaId, slug, itemId) = await SeedAsync();
        await using var provider = BuildProvider();

        Guid pedidoId = default;
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            var resultado = await scope.ServiceProvider.GetRequiredService<IniciarCheckoutGuestUseCase>().ExecuteAsync(
                Entrada(slug, itemId, qtd: 2));
            pedidoId = resultado.PedidoId;
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        var pedido = await leitura.Pedidos.AsNoTracking().Include(p => p.Itens).SingleAsync(p => p.Id == pedidoId);
        pedido.Status.Should().Be(StatusPedidoMapper.AguardandoAprovacaoBaba);
        pedido.Itens.Should().ContainSingle();
        pedido.Total.Valor.Should().Be(60m, "2 x R$ 30, cada item somado uma vez");
    }

    [SkippableTheory]
    [InlineData("+5511987654321", null)]
    [InlineData("11987654321", null)]
    [InlineData("5511987654321", null)]
    [InlineData(null, "+5511987654321")]
    public async Task ClienteBloqueadoPeloTelefoneEmOutroCadastro_NaoPassaPeloSiteSemLogin(string? telefoneDoCadastro, string? telefoneExtra)
    {
        // PEDIDO-06: o bloqueio feito no console/WhatsApp tem outro hash e nao pegava o guest.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();
        var (empresaId, slug, itemId) = await SeedAsync();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresaId);
            var bloqueado = Cliente.CriarParaStorefront(empresaId, $"hash-{Guid.NewGuid():N}", TimeProvider.System);
            bloqueado.Nome = "Calote";
            bloqueado.Telefone = telefoneDoCadastro;
            if (telefoneExtra is not null)
                bloqueado.Telefones.Add(new ClienteTelefone { Id = Guid.NewGuid(), Numero = telefoneExtra, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            bloqueado.Bloquear("calote", DateTime.UtcNow);
            seed.Clientes.Add(bloqueado);
            await seed.SaveChangesAsync();
        }
        await using var provider = BuildProvider();

        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        var act = () => scope.ServiceProvider.GetRequiredService<IniciarCheckoutGuestUseCase>().ExecuteAsync(
            Entrada(slug, itemId, qtd: 1, telefone: "5511987654321"));

        await act.Should().ThrowAsync<EasyStock.Domain.Exceptions.ClienteBloqueadoException>();
        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        (await leitura.Pedidos.CountAsync(p => p.EmpresaId == empresaId)).Should().Be(0);
    }

    private static IniciarCheckoutGuestInput Entrada(string slug, Guid itemId, int qtd, string telefone = "(11) 98765-4321") => new(
        Slug: slug, Nome: "Maria Silva", Telefone: telefone, Cep: Cep, Numero: "120",
        Items: [new CheckoutItemInput(itemId, qtd)], JanelaId: null, DataEntrega: null);

    private async Task<(Guid EmpresaId, string Slug, Guid ItemId)> SeedAsync()
    {
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        await using var seed = fixture.CreateDbContext();
        seed.SetMobileTenantContext(empresaId);
        seed.Set<Empresa>().Add(new Empresa
        {
            Id = empresaId, Nome = "Empresa Guest", Documento = $"{Random.Shared.Next(100000, 999999)}", CriadoEm = agora, AlteradoEm = agora
        });
        seed.Set<Categoria>().Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora });
        var produto = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Raviol", Status = StatusProduto.Ativo,
            PrecoReferencia = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(30m), CriadoEm = agora, AlteradoEm = agora
        };
        seed.Set<Produto>().Add(produto);
        var slug = $"sf-guest-{Guid.NewGuid():N}"[..30];
        var vitrine = StorefrontEntity.Criar(empresaId, slug, "Vitrine", 0m);
        vitrine.Ativar();
        seed.Storefronts.Add(vitrine);
        var item = CardapioItem.CriarAPartirDeProduto(vitrine.Id, produto);
        item.TornarVisivel();
        seed.Set<CardapioItem>().Add(item);
        await seed.SaveChangesAsync();
        return (empresaId, slug, item.Id);
    }

    private ServiceProvider BuildProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Acompanhamento:JwtSecret"] = new string('s', 40) })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddMemoryCache();
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddSingleton(Substitute.For<ICepLookupClient>());
        services.AddSingleton(Substitute.For<IGeocodingClient>());
        services.AddSingleton(Substitute.For<IRotaClient>());
        services.AddSingleton(Substitute.For<IMercadoPagoClient>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}
