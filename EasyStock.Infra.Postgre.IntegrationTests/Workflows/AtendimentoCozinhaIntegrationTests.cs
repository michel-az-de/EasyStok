using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.Operacao.Kds;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

[Collection("PostgreSqlTestCollection")]
public sealed class AtendimentoCozinhaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableTheory]
    [InlineData("online")]
    [InlineData("na_entrega")]
    public async Task ComandaDaConversa_EntraNaCozinhaEPersisteAtePronto(string forma)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var cenario = await SemearAsync();
        var avisos = new List<string>();
        var eventos = Substitute.For<IOperacaoEventPublisher>();
        eventos.PublicarAsync(Arg.Any<string>(), cenario.EmpresaId, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                // Outra conexão só vê a mudança depois do COMMIT da transação, não de SaveChanges.
                if (call.Arg<object>() is PedidoMudouStatusOperacao mudanca)
                {
                    await using var leitura = fixture.CreateDbContext();
                    leitura.SetMobileTenantContext(cenario.EmpresaId);
                    (await leitura.Pedidos.SingleAsync(p => p.Id == mudanca.PedidoId)).Status
                        .Should().Be(mudanca.StatusNovo);
                }
                avisos.Add(call.Arg<string>());
            });
        await using var provider = CriarProvider(eventos);
        var input = new GerarPedidoConversaInput(cenario.EmpresaId, cenario.ConversaId,
            [new ItemPedidoCheckout(cenario.ItemId, 2, "Sem molho")], cenario.JanelaId, cenario.Data, Forma: forma);
        Guid pedidoId;
        await using (var scope = provider.CreateAsyncScope())
        {
            DefinirEmpresa(scope, cenario.EmpresaId);
            var resultado = await scope.ServiceProvider.GetRequiredService<GerarPedidoConversaUseCase>().ExecuteAsync(input);
            resultado.Total.Should().Be(25m);
            pedidoId = resultado.PedidoId;
        }

        if (forma == "online")
        {
            (await LerFilaAsync(provider, cenario)).Should().BeEmpty("pedido ainda não pago não aparece na cozinha");
            await using var scope = provider.CreateAsyncScope();
            DefinirEmpresa(scope, cenario.EmpresaId);
            var confirmar = scope.ServiceProvider.GetRequiredService<ConfirmarPagamentoPedidoUseCase>();
            var pagamento = new ConfirmarPagamentoPedidoInput(pedidoId, "pagamento-jornada", "approved", 25m,
                "pix", ReferenciaExterna: "preferencia-jornada");
            (await confirmar.ExecuteAsync(pagamento)).Confirmado.Should().BeTrue();
            (await confirmar.ExecuteAsync(pagamento)).Situacao.Should().Be(SituacaoConfirmacaoPagamento.JaConfirmado);
            avisos.Count(e => e == EventosOperacao.PedidoPago).Should().Be(1);
        }
        else
        {
            avisos.Should().ContainSingle(e => e == EventosOperacao.PedidoMudouStatus);
            avisos.Should().NotContain(EventosOperacao.PedidoPago, "na entrega não significa pago");
            await using var scope = provider.CreateAsyncScope();
            DefinirEmpresa(scope, cenario.EmpresaId);
            await scope.ServiceProvider.GetRequiredService<TrocarFormaPagamentoPedidoUseCase>().ExecuteAsync(
                new TrocarFormaPagamentoPedidoInput(cenario.EmpresaId, pedidoId, forma));
            avisos.Should().ContainSingle(e => e == EventosOperacao.PedidoMudouStatus);
        }

        var card = (await LerFilaAsync(provider, cenario)).Should().ContainSingle().Subject;
        card.Id.Should().Be(pedidoId);
        card.Status.Should().Be("aguardando");
        card.InicioPrevistoEm.Should().NotBeNull();
        card.Itens.Should().Contain(i => i.Nome == "brigadeiro" && i.Qtd == 2 && i.Observacao == "Sem molho");
        (await LerFilaAsync(provider, cenario with { EmpresaId = Guid.NewGuid() })).Should().BeEmpty();

        // Uma nova requisição não pode criar um segundo pedido ou uma segunda vaga na mesma conversa.
        await using (var scope = provider.CreateAsyncScope())
        {
            DefinirEmpresa(scope, cenario.EmpresaId);
            var duplicar = () => scope.ServiceProvider.GetRequiredService<GerarPedidoConversaUseCase>().ExecuteAsync(input);
            await duplicar.Should().ThrowAsync<RegraDeDominioVioladaException>();
        }

        foreach (var status in new[] { "preparando", "pronto" })
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                DefinirEmpresa(scope, cenario.EmpresaId);
                await scope.ServiceProvider.GetRequiredService<AtualizarStatusPedidoUseCase>().ExecuteAsync(
                    new AtualizarStatusPedidoCommand(cenario.EmpresaId, pedidoId, status));
            }
            (await LerFilaAsync(provider, cenario)).Should().ContainSingle().Which.Status.Should().Be(status);
        }

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(cenario.EmpresaId);
        (await db.Pedidos.CountAsync()).Should().Be(1);
        (await db.VagasOcupadas.CountAsync(v => v.PedidoId == pedidoId && v.LiberadoEm == null)).Should().Be(1);
        (await db.ImpressoesPendentes.CountAsync(i => i.PedidoId == pedidoId)).Should().Be(forma == "online" ? 1 : 0);
        var pedido = await db.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedidoId);
        pedido.TotalPago.Should().Be(forma == "online" ? 25m : 0m);
        await using var consulta = provider.CreateAsyncScope();
        DefinirEmpresa(consulta, cenario.EmpresaId);
        var comanda = await consulta.ServiceProvider.GetRequiredService<ObterPedidoConversaUseCase>()
            .ExecuteAsync(cenario.EmpresaId, cenario.ConversaId);
        comanda!.Status.Should().Be("pronto");
        comanda.Cobranca!.Status.Should().Be(forma == "online" ? "Paga" : "Pendente");
    }

    private static void DefinirEmpresa(AsyncServiceScope scope, Guid empresaId) =>
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);

    private static async Task<IReadOnlyList<KdsPedidoDto>> LerFilaAsync(ServiceProvider provider, Cenario cenario)
    {
        await using var scope = provider.CreateAsyncScope();
        DefinirEmpresa(scope, cenario.EmpresaId);
        return await scope.ServiceProvider.GetRequiredService<ListarPedidosKdsUseCase>()
            .ExecuteAsync(new ListarPedidosKdsQuery(cenario.EmpresaId, Data: cenario.Data));
    }

    private sealed record Cenario(Guid EmpresaId, Guid ConversaId, Guid ItemId, Guid JanelaId, DateOnly Data);

    private async Task<Cenario> SemearAsync()
    {
        var empresaId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa { Id = empresaId, Nome = "Jornada cozinha", Documento = empresaId.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
        var loja = StorefrontEntity.Criar(empresaId, $"jornada-{empresaId:N}"[..20], "Jornada cozinha", 0m);
        loja.Ativar();
        db.Storefronts.Add(loja);
        var data = DateOnly.FromDateTime(agora).AddDays(7);
        var janela = JanelaEntrega.Criar(loja.Id, (int)data.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(12, 0), 10, "Manhã");
        db.JanelasEntrega.Add(janela);
        db.FreteZonas.Add(FreteZona.CriarPorCep(loja.Id, "Centro", "01000000", "01999999", 5m, 60));
        var item = CardapioItem.CriarAvulso(loja.Id, "Brigadeiro", 10m);
        item.TornarVisivel();
        db.CardapioItens.Add(item);
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Cliente de teste", Telefone = "11999998888" };
        cliente.Enderecos.Add(new ClienteEndereco { Id = Guid.NewGuid(), ClienteId = cliente.Id, Logradouro = "Rua A", Numero = "10", Cep = "01310100", Padrao = true, CriadoEm = agora, AlteradoEm = agora });
        db.Clientes.Add(cliente);
        var conversa = Conversa.Abrir(empresaId, "5511999998888", agora, cliente.Nome, cliente.Id);
        db.AtendimentoConversas.Add(conversa);
        await db.SaveChangesAsync();
        return new Cenario(empresaId, conversa.Id, item.Id, janela.Id, data);
    }

    private ServiceProvider CriarProvider(IOperacaoEventPublisher eventos)
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(eventos);
        services.AddSingleton(Substitute.For<ICepLookupClient>());
        services.AddSingleton(Substitute.For<IGeocodingClient>());
        services.AddSingleton(Substitute.For<IRotaClient>());
        services.AddSingleton(Substitute.For<IEstornoPedidoGateway>());
        var mp = Substitute.For<IMercadoPagoClient>();
        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new PreferenceCriadaResult("preferencia-jornada", "https://mp.test/jornada"));
        services.AddSingleton(mp);
        return services.BuildServiceProvider();
    }
}
