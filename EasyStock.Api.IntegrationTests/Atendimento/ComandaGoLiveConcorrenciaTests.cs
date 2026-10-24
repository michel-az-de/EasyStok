using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using FluentAssertions;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EasyStock.Application.Ports.Output;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task Comanda_QuatroClientesDoisAtendentesIsolamTenantERepetemMesmoCallback()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var identidades = IdentidadesGoLive();
        await using var factory = CriarFactoryGoLive(identidades);
        factory.UseKestrel(0);
        using var cliente = factory.CreateClient();
        using var operador = factory.CreateClient(); operador.DefaultRequestHeaders.Add(CabecalhoGoLive, identidades[0].UsuarioId.ToString());
        using var gerente = factory.CreateClient(); gerente.DefaultRequestHeaders.Add(CabecalhoGoLive, identidades[1].UsuarioId.ToString());
        using var externo = factory.CreateClient(); externo.DefaultRequestHeaders.Add(CabecalhoGoLive, identidades[2].UsuarioId.ToString());
        var cadastro = await SemearGoLive(factory, identidades);
        var primeiro = cadastro.Clientes[0];
        var lista = $"/api/atendimento/conversas/{primeiro.ConversaId}/comandas-cliente";
        (await cliente.GetAsync(lista)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await operador.GetAsync(lista)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await gerente.GetAsync(lista)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await externo.GetAsync(lista)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await operador.GetAsync($"/api/atendimento/conversas/{cadastro.OutroTenant.ConversaId}/comandas-cliente"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var (pessoa, indice) in cadastro.Clientes.Select((p, i) => (p, i)))
        {
            var estado = await Estado(cliente, pessoa.Token);
            estado.ConversaId.Should().Be(pessoa.ConversaId);
            estado.NumeroComanda.Should().Be(pessoa.NumeroComanda);
            var rascunho = estado.Rascunho with { Itens = [new(cadastro.Cenario.CardapioItemId, 1)],
                JanelaId = cadastro.Cenario.JanelaId, DataEntrega = cadastro.Cenario.DataEntrega };
            if (indice == 0)
            {
                var salvos = await Task.WhenAll(cliente.PutAsJsonAsync($"{Rota(pessoa.Token)}/rascunho", new { estado.Revisao, rascunho }),
                    operador.PutAsJsonAsync($"{lista}/{estado.LinkId}/rascunho", new { estado.Revisao, rascunho }),
                    gerente.PutAsJsonAsync($"{lista}/{estado.LinkId}/rascunho", new { estado.Revisao, rascunho }));
                salvos.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.Conflict]);
            }
            else await Salvar(cliente, pessoa.Token, estado, rascunho);
            estado = await Estado(cliente, pessoa.Token);
            var envios = await Task.WhenAll(Enviar(cliente, pessoa.Token, estado.Revisao), Enviar(cliente, pessoa.Token, estado.Revisao));
            envios.Select(e => e.PedidoId).Distinct().Should().ContainSingle();
            var pedidoId = envios[0].PedidoId;
            (await operador.PostAsJsonAsync($"/api/pedidos/{pedidoId}/estornos-online",
                new { operacaoId = Guid.NewGuid(), pagamentoId = Guid.NewGuid(), valor = 1, motivo = "Controle sem privilégio" }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var callback = new { pedidoId, valor = 15, pagamentoExternoId = $"82000{indice}" };
            var pagos = await Task.WhenAll(cliente.PostAsJsonAsync("/__comanda-teste/pagamento", callback), cliente.PostAsJsonAsync("/__comanda-teste/pagamento", callback));
            foreach (var pago in pagos) pago.StatusCode.Should().Be(HttpStatusCode.OK, await pago.Content.ReadAsStringAsync());
            var quitado = await Estado(cliente, pessoa.Token);
            quitado.Financeiro!.Recebido.Should().Be(15); quitado.Financeiro.Alocado.Should().Be(15);
            quitado.Pedido!.Pagamentos.Should().ContainSingle();
        }
        cadastro.Clientes.Select(c => c.NumeroComanda).Should().OnlyHaveUniqueItems();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.Pedidos.CountAsync()).Should().Be(4);
        (await db.CobrancasPedido.CountAsync()).Should().Be(4);
        (await db.Pedidos.SelectMany(p => p.Pagamentos).CountAsync()).Should().Be(4);
    }

    [SkippableTheory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Comanda_ObservacaoGeralAlteradaExigeConferenciaMesmoComAcrescimo(int quantidade)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 3)],
            Observacoes = "Entregar na portaria", JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        estado = await Estado(client, token);
        var envio = await Enviar(client, token, estado.Revisao);
        await ConfirmarPagamento(factory, envio.PedidoId, "810001", 35);
        var observacaoOriginal = await Observacao();
        using var nova = await client.PostAsync($"{Rota(token)}/nova-edicao", null);
        nova.StatusCode.Should().Be(HttpStatusCode.OK);
        var link = (await nova.Content.ReadFromJsonAsync<LinkCardapioConversaGerado>())!;
        estado = await Estado(client, link.Token);
        // #2007 (item 11): o cliente lê o que escreveu; o retrato "[Entrega] ..." fica só no pedido, para a equipe.
        observacaoOriginal.Should().Contain("[Entrega]");
        estado.Rascunho.Observacoes.Should().Be("Entregar na portaria", "o novo link conserva a observação já registrada");
        estado.Pedido!.Observacoes.Should().Be("Entregar na portaria");
        await Salvar(client, link.Token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, quantidade)],
            Observacoes = "Ligar antes, não deixar na portaria" });
        estado = await Estado(client, link.Token);
        var proposta = await Enviar(client, link.Token, estado.Revisao);
        proposta.Situacao.Should().Be("alteracao_aguardando_confirmacao", "observação geral exige a mesma conferência das notas dos itens");
        var aguardando = await Estado(client, link.Token);
        aguardando.Pedido!.Total.Should().Be(35);
        (await Observacao()).Should().Be(observacaoOriginal);
        using var aprovado = await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar", new { aceitar = true });
        aprovado.StatusCode.Should().Be(HttpStatusCode.OK, await aprovado.Content.ReadAsStringAsync());
        (await Observacao()).Should().Be(EnderecoCheckout.ComRetratoDaEntregaDe(observacaoOriginal, "Ligar antes, não deixar na portaria"))
            .And.StartWith("Ligar antes, não deixar na portaria | [Entrega] ", "aprovar a alteração não apaga o endereço gravado no checkout");

        async Task<string?> Observacao()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().Pedidos.IgnoreQueryFilters()
                .Where(p => p.EmpresaId == EmpresaId && p.Id == envio.PedidoId).Select(p => p.Observacoes).SingleAsync();
        }
    }
}
