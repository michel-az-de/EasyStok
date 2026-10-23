using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task Comanda_RascunhoAnteriorRecebeEnderecoDoCadastro()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, new([new(c.CardapioItemId, 1)]));
        var relido = await Estado(client, token);
        relido.Rascunho.Endereco.Should().NotBeNull();
        relido.Rascunho.Endereco!.Cep.Should().Be("01310100");
        relido.Rascunho.Endereco.Numero.Should().Be("10");
        relido.PrecisaCadastro.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Comanda_VisitanteComPedidoDoSiteNaoDerrubaOsLinksNemExpoeOPedido()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var criado = await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/pedido", new
        {
            itens = new[] { new { cardapioItemId = c.CardapioItemId, qtd = 1 } },
            janelaId = c.JanelaId, dataEntrega = c.DataEntrega.ToString("yyyy-MM-dd"), forma = "na_entrega",
        });
        criado.StatusCode.Should().Be(HttpStatusCode.OK, await criado.Content.ReadAsStringAsync());
        Guid visitanteId;
        using (var scope = factory.Services.CreateScope())
        {
            // #1865: o checkout do site liga o pedido ao chat sem conceder identidade à conversa do visitante.
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var pedidoId = (await db.AtendimentoConversas.SingleAsync(x => x.Id == c.ConversaId)).PedidoEmAndamentoId!.Value;
            var visitante = Conversa.Abrir(EmpresaId, $"site:{Guid.NewGuid():N}", DateTime.UtcNow, "Visitante do site",
                canal: EasyStock.Domain.Enums.Atendimento.CanalConversa.ChatSite);
            visitante.DefinirPedidoEmAndamento(pedidoId);
            db.AtendimentoConversas.Add(visitante);
            await db.SaveChangesAsync();
            visitanteId = visitante.Id;
        }
        var token = await Link(factory, visitanteId);

        using var lista = await client.GetAsync($"/api/atendimento/conversas/{visitanteId}/comandas-cliente");
        lista.StatusCode.Should().Be(HttpStatusCode.OK, await lista.Content.ReadAsStringAsync());
        var estado = await Estado(client, token);
        estado.Pedido.Should().BeNull("o visitante não comprovou ser o dono do pedido do site");
        estado.PodeEditar.Should().BeTrue();
        await Salvar(client, token, estado, new([new(c.CardapioItemId, 2)], JanelaId: c.JanelaId, DataEntrega: c.DataEntrega,
            Endereco: new("01310100", "Rua do visitante", "7", "Centro", "São Paulo", "SP", null),
            Nome: "Visitante identificado", Telefone: "11912340000"));
        var enviado = await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { (await Estado(client, token)).Revisao });
        enviado.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o pedido do site segue em andamento nesta conversa");
        (await enviado.Content.ReadAsStringAsync()).Should().Contain("já tem um pedido em andamento");
        (await client.GetAsync($"/api/atendimento/conversas/{visitanteId}/comandas-cliente")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableTheory]
    [InlineData("01310100", false)]
    [InlineData("20040002", true)]
    public async Task Comanda_LeadCadastraEnderecoEEntregaForaDaAreaVaiParaHumano(string cep, bool fora)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var cobrancas = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        await using var factory = CriarFactory(Provedor(cobrancas));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        Guid conversaId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var lead = Conversa.Abrir(EmpresaId, "5511912345678", DateTime.UtcNow, "Cliente da sprint");
            db.AtendimentoConversas.Add(lead);
            await db.SaveChangesAsync();
            conversaId = lead.Id;
        }
        var token = await Link(factory, conversaId);
        var estado = await Estado(client, token);
        estado.PrecisaCadastro.Should().BeTrue();
        estado.PodeEditar.Should().BeTrue();
        await Salvar(client, token, estado, new([new(c.CardapioItemId, 2)], JanelaId: c.JanelaId,
            DataEntrega: c.DataEntrega, Endereco: new(cep, "Rua da sprint", "42", "Centro", "São Paulo", "SP", "Casa 2"),
            Nome: "Cliente da sprint", Telefone: "11912345678"));
        estado = await Estado(client, token);
        var enviado = await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { estado.Revisao });
        enviado.StatusCode.Should().Be(HttpStatusCode.OK, await enviado.Content.ReadAsStringAsync());
        var final = await Estado(client, token);
        final.PrecisaCadastro.Should().BeFalse();
        final.EntregaACombinar.Should().Be(fora);
        final.Rascunho.Endereco!.Numero.Should().Be("42");
        using var verificacao = factory.Services.CreateScope();
        var banco = verificacao.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var conversa = await banco.AtendimentoConversas.SingleAsync(x => x.Id == conversaId);
        var cliente = await banco.Clientes.Include(x => x.Enderecos).SingleAsync(x => x.Id == conversa.ClienteId);
        cliente.Nome.Should().Be("Cliente da sprint");
        cliente.Id.Should().NotBe(c.ClienteId, "telefone informado em link público não comprova a identidade do cadastro anterior");
        (await banco.Clientes.SingleAsync(x => x.Id == c.ClienteId)).Nome.Should().Be("Maria");
        cliente.Enderecos.Should().Contain(x => x.Cep == cep && x.Numero == "42" && x.Cidade == "São Paulo");
        if (fora)
        {
            final.Pedido.Should().BeNull();
            final.Rascunho.Itens.Should().ContainSingle().Which.Qtd.Should().Be(2);
            cobrancas.Should().BeEmpty();
            conversa.Situacao.Should().Be(EasyStock.Domain.Enums.Atendimento.SituacaoConversa.Assumida);
            var notasAntes = await banco.AtendimentoMensagens.CountAsync(x => x.ConversaId == conversaId);
            (await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { final.Revisao })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await banco.AtendimentoMensagens.CountAsync(x => x.ConversaId == conversaId)).Should().Be(notasAntes);
            var rotaFrete = $"/api/atendimento/conversas/{conversaId}/comandas-cliente/{final.LinkId}/combinar-frete";
            (await client.PostAsJsonAsync(rotaFrete, new { final.Revisao })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.PostAsJsonAsync(rotaFrete, new { final.Revisao, Valor = -1m })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{final.LinkId}/combinar-frete",
                new { final.Revisao, Valor = 13.5m })).StatusCode.Should().Be(HttpStatusCode.Gone);
            (await client.PostAsJsonAsync(rotaFrete, new { final.Revisao, Valor = 13.5m })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.PostAsJsonAsync(rotaFrete, new { final.Revisao, Valor = 13.5m })).StatusCode.Should().Be(HttpStatusCode.OK);
            var combinado = await Estado(client, token);
            combinado.EntregaACombinar.Should().BeFalse();
            combinado.FreteCombinado.Should().Be(13.5m);
            combinado.TotalPrevisto.Should().Be(33.5m);
            var janelaAtual = await banco.JanelasEntrega.SingleAsync(j => j.Id == c.JanelaId);
            var outraJanela = EasyStock.Domain.Entities.Storefront.JanelaEntrega.Criar(janelaAtual.StorefrontId,
                janelaAtual.DiaDaSemana, new TimeOnly(14, 0), new TimeOnly(17, 0), 10, "Tarde");
            banco.JanelasEntrega.Add(outraJanela);
            await banco.SaveChangesAsync();
            await Salvar(client, token, combinado, combinado.Rascunho with { JanelaId = outraJanela.Id });
            combinado = await Estado(client, token);
            combinado.FreteCombinado.Should().Be(13.5m, "trocar somente o horário no mesmo dia preserva o frete combinado");
            combinado.EntregaACombinar.Should().BeFalse();
            combinado.TotalPrevisto.Should().Be(33.5m);
            await Salvar(client, token, combinado, combinado.Rascunho with { DataEntrega = combinado.Rascunho.DataEntrega!.Value.AddDays(7) });
            combinado = await Estado(client, token);
            combinado.FreteCombinado.Should().Be(13.5m, "trocar só a data preserva o frete combinado (#1717-6)");
            combinado.TotalPrevisto.Should().Be(33.5m);
            await Salvar(client, token, combinado, combinado.Rascunho with { Observacoes = "Conferir transporte novamente" });
            var alterado = await Estado(client, token);
            alterado.FreteCombinado.Should().BeNull();
            (await client.PostAsJsonAsync(rotaFrete, new { combinado.Revisao, Valor = 13.5m })).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { alterado.Revisao })).StatusCode.Should().Be(HttpStatusCode.OK);
            cobrancas.Should().BeEmpty();
            (await client.PostAsJsonAsync(rotaFrete, new { alterado.Revisao, Valor = 13.5m })).StatusCode.Should().Be(HttpStatusCode.OK);
            combinado = await Estado(client, token);
            await Salvar(client, token, combinado, combinado.Rascunho with { Endereco = combinado.Rascunho.Endereco! with { Numero = "43" } });
            alterado = await Estado(client, token);
            alterado.FreteCombinado.Should().BeNull("mudar o destino exige conferir a cotação novamente");
            await Salvar(client, token, alterado, combinado.Rascunho);
            alterado = await Estado(client, token);
            alterado.FreteCombinado.Should().BeNull("restaurar o destino não recupera uma cotação invalidada");
            (await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { alterado.Revisao })).StatusCode.Should().Be(HttpStatusCode.OK);
            cobrancas.Should().BeEmpty();
            (await client.PostAsJsonAsync(rotaFrete, new { alterado.Revisao, Valor = 13.5m })).StatusCode.Should().Be(HttpStatusCode.OK);
            combinado = await Estado(client, token);
            var confirmado = await Enviar(client, token, combinado.Revisao);
            confirmado.Total.Should().Be(33.5m);
            (await Estado(client, token)).Pedido!.Frete.Should().Be(13.5m);
            cobrancas.Should().ContainSingle();
        }
        else
        {
            final.Pedido.Should().NotBeNull();
            final.Pedido!.Frete.Should().Be(5m);
            final.Pedido.Total.Should().Be(25m);
            cobrancas.Should().ContainSingle();
        }
    }

    [SkippableFact]
    public async Task Comanda_NumeroEstavelNovaEdicaoPreservaSelecaoERecuperaRespostaPerdida()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        await using var factory = CriarFactory(Provedor(preferencias));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var primeiro = await Gerar(); var inicial = await Estado(client, primeiro.Token);
        inicial.NumeroComanda.Should().BePositive(); inicial.ConversaId.Should().Be(c.ConversaId);
        var listaInicial = await ListarNoAtendimento();
        listaInicial.Should().ContainSingle(e => e.LinkId == inicial.LinkId && e.Revisao == 0 && e.PodeEditar,
            "o atendimento já pode editar a seleção antes do primeiro salvamento do cliente");
        await Salvar(client, primeiro.Token, inicial, inicial.Rascunho with { Itens = [new(c.CardapioItemId, 1)], Observacoes = "Preservar seleção",
            JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var segundo = await Gerar(); segundo.NumeroComanda.Should().Be(inicial.NumeroComanda);
        (await client.GetAsync($"{Rota(primeiro.Token)}/comanda")).StatusCode.Should().Be(HttpStatusCode.Gone);
        var vigente = await Estado(client, segundo.Token);
        vigente.Rascunho.Observacoes.Should().Be("Preservar seleção"); vigente.Rascunho.Itens.Should().ContainSingle();
        var operador = client.PutAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{vigente.LinkId}/rascunho",
            new { vigente.Revisao, Rascunho = vigente.Rascunho with { Observacoes = "Edição do atendimento" } });
        var cliente = client.PutAsJsonAsync($"{Rota(segundo.Token)}/rascunho",
            new { vigente.Revisao, Rascunho = vigente.Rascunho with { Observacoes = "Edição do cliente" } });
        var concorrentes = await Task.WhenAll(operador, cliente);
        concorrentes.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        vigente = await Estado(client, segundo.Token);
        var envios = await Task.WhenAll(Enviar(client, segundo.Token, vigente.Revisao), Enviar(client, segundo.Token, vigente.Revisao));
        envios.Select(e => e.PedidoId).Distinct().Should().ContainSingle(); preferencias.Should().ContainSingle();
        var novas = await Task.WhenAll(client.PostAsync($"{Rota(segundo.Token)}/nova-edicao", null), client.PostAsync($"{Rota(segundo.Token)}/nova-edicao", null));
        foreach (var resposta in novas) resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        var links = await Task.WhenAll(novas.Select(r => r.Content.ReadFromJsonAsync<EasyStock.Application.Services.Atendimento.LinkCardapioConversaGerado>()));
        links.Select(l => l!.Token).Distinct().Should().ContainSingle("repetir após perder a resposta recupera o mesmo link");
        var atual = await Estado(client, links[0]!.Token);
        atual.NumeroComanda.Should().Be(inicial.NumeroComanda); atual.Pedido!.NumeroComanda.Should().Be(inicial.NumeroComanda);
        atual.Pedido.PedidoId.Should().Be(envios[0].PedidoId);
        var listaAtual = await ListarNoAtendimento();
        listaAtual.Should().Contain(e => e.LinkId == atual.LinkId && e.Revisao == 0 && e.PodeEditar,
            "a nova edição do cliente aparece imediatamente para o atendimento");
        listaAtual.Should().Contain(e => e.LinkId == vigente.LinkId && !e.PodeEditar,
            "o envio anterior permanece como comprovante");
        (await Estado(client, segundo.Token)).PodeEditar.Should().BeFalse("o link consumido acompanha o pedido");
        await Enviar(client, segundo.Token, vigente.Revisao);
        preferencias.Should().ContainSingle();
        using (var mensagensScope = factory.Services.CreateScope())
        {
            var textos = await mensagensScope.ServiceProvider.GetRequiredService<EasyStockDbContext>().AtendimentoMensagens.Select(m => m.Texto).ToListAsync();
            // #1717-7 (#1961): a encomenda pela comanda cobra o sinal, como o console; antes cobrava o saldo integral.
            textos.Should().Contain(t => t!.Contains("Sinal agora por link"));
            textos.Should().ContainSingle(t => t!.Contains("Recebemos seu pedido") && t.Contains("Restante na entrega"));
            textos.Should().NotContain(t => t!.Contains("saldo integral"));
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.Pedidos.CountAsync()).Should().Be(1);
        var numeros = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            using var escopo = factory.Services.CreateScope();
            var banco = escopo.ServiceProvider.GetRequiredService<EasyStockDbContext>(); banco.SetMobileTenantContext(EmpresaId);
            var conversa = Conversa.Abrir(EmpresaId, $"551198765000{i}", DateTime.UtcNow);
            banco.AtendimentoConversas.Add(conversa); await banco.SaveChangesAsync(); return conversa.NumeroComanda;
        }));
        numeros.Should().OnlyHaveUniqueItems().And.OnlyContain(n => n > 0 && n != inicial.NumeroComanda);

        async Task<EasyStock.Application.Services.Atendimento.LinkCardapioConversaGerado> Gerar()
        {
            var resposta = await client.PostAsync($"/api/atendimento/conversas/{c.ConversaId}/link-cardapio", null);
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
            var json = System.Text.Json.JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("data").Deserialize<EasyStock.Application.Services.Atendimento.LinkCardapioConversaGerado>(ComandaClienteUseCase.Json)!;
        }

        async Task<IReadOnlyList<ComandaClienteResult>> ListarNoAtendimento()
        {
            using var resposta = await client.GetAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente");
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("data").Deserialize<List<ComandaClienteResult>>(ComandaClienteUseCase.Json)!;
        }
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Comanda_ReciboAntigoRecuperaEdicaoAposFilhoConsumidoOuSubstituido(bool consumirFilho)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var tokenPai = await Link(factory, c.ConversaId);
        var pai = await Estado(client, tokenPai);
        await Salvar(client, tokenPai, pai, pai.Rascunho with { Itens = [new(c.CardapioItemId, 1)],
            JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        pai = await Estado(client, tokenPai);
        var pedidoId = (await Enviar(client, tokenPai, pai.Revisao)).PedidoId;
        await ConfirmarPagamento(factory, pedidoId, "830001", 15);
        using var primeiraResposta = await client.PostAsync($"{Rota(tokenPai)}/nova-edicao", null);
        primeiraResposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var filho = (await primeiraResposta.Content.ReadFromJsonAsync<EasyStock.Application.Services.Atendimento.LinkCardapioConversaGerado>())!;
        var estadoFilho = await Estado(client, filho.Token);
        await Salvar(client, filho.Token, estadoFilho, estadoFilho.Rascunho with { Itens = [new(c.CardapioItemId, 2)] });
        if (consumirFilho)
        {
            estadoFilho = await Estado(client, filho.Token);
            (await Enviar(client, filho.Token, estadoFilho.Revisao)).PedidoId.Should().Be(pedidoId);
            await ConfirmarPagamento(factory, pedidoId, "830002", 10);
        }
        using var compartilhamento = await client.PostAsync($"/api/atendimento/conversas/{c.ConversaId}/link-cardapio", null);
        compartilhamento.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await compartilhamento.Content.ReadAsStringAsync());
        var vigente = json.RootElement.GetProperty("data").Deserialize<EasyStock.Application.Services.Atendimento.LinkCardapioConversaGerado>(ComandaClienteUseCase.Json)!;
        var selecao = await Estado(client, vigente.Token);
        await Salvar(client, vigente.Token, selecao, selecao.Rascunho with { Itens = [new(c.CardapioItemId, 3)], Observacoes = "Seleção vigente preservada" });
        (await client.GetAsync($"{Rota(tokenPai)}/comanda")).StatusCode.Should().Be(HttpStatusCode.OK);
        var antes = await Contar();
        using var resposta = await client.PostAsync($"{Rota(tokenPai)}/nova-edicao", null);
        var corpo = await resposta.Content.ReadAsStringAsync();
        var depois = await Contar();
        var evidencia = new { linkPai = pai.LinkId, linkFilho = estadoFilho.LinkId, consumirFilho,
            linksAntes = antes.Links, linksDepois = depois.Links, pedidosAntes = antes.Pedidos, pedidosDepois = depois.Pedidos,
            cobrancasAntes = antes.Cobrancas, cobrancasDepois = depois.Cobrancas,
            pagamentosAntes = antes.Pagamentos, pagamentosDepois = depois.Pagamentos,
            financeiroAntes = selecao.Financeiro, financeiroDepois = (await Estado(client, tokenPai)).Financeiro };
        resposta.StatusCode.Should().Be(HttpStatusCode.OK,
            $"o recibo continua acessível após seu filho; HTTP={(int)resposta.StatusCode}; corpo={corpo}; evidencia={JsonSerializer.Serialize(evidencia)}");
        var nova = (await resposta.Content.ReadFromJsonAsync<EasyStock.Application.Services.Atendimento.LinkCardapioConversaGerado>())!;
        var repeticoes = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => client.PostAsync(
            $"{Rota(consumirFilho && i % 2 == 1 ? filho.Token : tokenPai)}/nova-edicao", null)));
        foreach (var repeticao in repeticoes)
        {
            repeticao.StatusCode.Should().Be(HttpStatusCode.OK, await repeticao.Content.ReadAsStringAsync());
            (await repeticao.Content.ReadFromJsonAsync<EasyStock.Application.Services.Atendimento.LinkCardapioConversaGerado>())!.Token.Should().Be(nova.Token);
        }
        var recuperada = await Estado(client, nova.Token);
        recuperada.PodeEditar.Should().BeTrue();
        recuperada.Pedido!.PedidoId.Should().Be(pedidoId); recuperada.NumeroComanda.Should().Be(pai.NumeroComanda);
        recuperada.Rascunho.Observacoes.Should().Be("Seleção vigente preservada");
        recuperada.Rascunho.Itens.Should().ContainSingle().Which.Qtd.Should().Be(3);
        recuperada.Financeiro!.Recebido.Should().Be(consumirFilho ? 25 : 15);
        recuperada.Financeiro.Alocado.Should().Be(consumirFilho ? 25 : 15);
        (await client.GetAsync($"{Rota(vigente.Token)}/comanda")).StatusCode.Should().Be(HttpStatusCode.Gone);
        var final = await Contar();
        final.Links.Should().Be(antes.Links + 1); final.Editaveis.Should().Be(1);
        final.Pedidos.Should().Be(1); final.Cobrancas.Should().Be(antes.Cobrancas); final.Pagamentos.Should().Be(antes.Pagamentos);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>(); db.SetMobileTenantContext(EmpresaId);
            var expirado = await db.LinksCardapioConversa.SingleAsync(l => l.Id == pai.LinkId);
            expirado.Expirar(DateTime.UtcNow.AddMinutes(-1)); await db.SaveChangesAsync();
        }
        (await client.PostAsync($"{Rota(tokenPai)}/nova-edicao", null)).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await Contar()).Links.Should().Be(final.Links);

        async Task<(int Links, int Editaveis, int Pedidos, int Cobrancas, int Pagamentos)> Contar()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>(); db.SetMobileTenantContext(EmpresaId);
            return (await db.LinksCardapioConversa.CountAsync(),
                await db.LinksCardapioConversa.CountAsync(l => l.UsadoEm == null && l.ExpiraEm > DateTime.UtcNow),
                await db.Pedidos.CountAsync(), await db.CobrancasPedido.CountAsync(),
                await db.Pedidos.SelectMany(p => p.Pagamentos).CountAsync());
        }
    }

    private static IMercadoPagoClient Provedor(ConcurrentDictionary<string, CriarPreferenceCommand> preferencias)
    {
        var mp = Substitute.For<IMercadoPagoClient>();
        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            var command = c.Arg<CriarPreferenceCommand>();
            preferencias.TryAdd(command.IdempotencyKey!, command);
            return new PreferenceCriadaResult(command.IdempotencyKey!, "https://www.mercadopago.com.br/checkout/teste");
        });
        return mp;
    }

    private static async Task<string> Link(WebApplicationFactory<Program> factory, Guid conversaId)
    {
        var token = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.LinksCardapioConversa.Add(LinkCardapioConversa.Gerar(EmpresaId, conversaId, AcessoChatSite.HashDoToken(token), DateTime.UtcNow));
        await db.SaveChangesAsync();
        return token;
    }

    private static string Rota(string token) => $"/api/storefront/cardapio-conversa/{token}";
    private static async Task<ComandaClienteResult> Estado(HttpClient client, string token) =>
        (await client.GetFromJsonAsync<ComandaClienteResult>($"{Rota(token)}/comanda"))!;
    private static async Task Salvar(HttpClient client, string token, ComandaClienteResult estado, RascunhoComandaCliente dados)
    {
        var resposta = await client.PutAsJsonAsync($"{Rota(token)}/rascunho", new { estado.Revisao, Rascunho = dados });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
    }
    private static async Task<EnvioComandaCliente> Enviar(HttpClient client, string token, int revisao)
    {
        var resposta = await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { revisao });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        return (await resposta.Content.ReadFromJsonAsync<EnvioComandaCliente>())!;
    }
    private static async Task ConfirmarPagamento(WebApplicationFactory<Program> factory, Guid pedidoId, string pagamentoId, decimal valor)
    {
        using var scope = factory.Services.CreateScope();
        var uc = scope.ServiceProvider.GetRequiredService<ConfirmarPagamentoPedidoUseCase>();
        var input = new ConfirmarPagamentoPedidoInput(pedidoId, pagamentoId, "approved", valor, "pix");
        (await uc.ExecuteAsync(input)).Situacao.Should().Be(SituacaoConfirmacaoPagamento.Confirmado);
        (await uc.ExecuteAsync(input)).Situacao.Should().Be(SituacaoConfirmacaoPagamento.JaConfirmado);
    }

    [SkippableFact]
    public async Task Comanda_SelecaoCompartilhadaEnvioRepetidoECobrancaSoDoAcrescimo()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        await using var factory = CriarFactory(Provedor(preferencias));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var produtoId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var seed = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            seed.SetMobileTenantContext(EmpresaId);
            var agora = DateTime.UtcNow;
            var categoriaId = Guid.NewGuid();
            seed.Set<Categoria>().Add(new Categoria { Id = categoriaId, EmpresaId = EmpresaId, Nome = "Doces", CriadoEm = agora, AlteradoEm = agora });
            seed.Set<Produto>().Add(new Produto { Id = produtoId, EmpresaId = EmpresaId, CategoriaId = categoriaId, Nome = "Brigadeiro", Status = StatusProduto.Ativo, CriadoEm = agora, AlteradoEm = agora });
            (await seed.CardapioItens.SingleAsync(i => i.Id == c.CardapioItemId)).VincularProduto(produtoId);
            await seed.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<RegistrarEntradaEstoqueUseCase>().ExecuteAsync(
                new RegistrarEntradaEstoqueCommand(EmpresaId, produtoId, null, 5, 1m, null, agora.AddDays(-1),
                    NaturezaMovimentacaoEstoque.Producao, null, null, null, null, null, null, null, null, null, null, null, null, null));
        }
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        var dados = estado.Rascunho with { Itens = [new(c.CardapioItemId, 1, "sem granulado")], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega };
        await Salvar(client, token, estado, dados);
        var stale = await client.PutAsJsonAsync($"{Rota(token)}/rascunho", new { Revisao = 0, Rascunho = dados });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().Pedidos.CountAsync()).Should().Be(0);
        var envios = await Task.WhenAll(Enviar(client, token, 1), Enviar(client, token, 1));
        envios.Select(e => e.PedidoId).Distinct().Should().ContainSingle();
        envios[0].Saldo.Should().Be(15);
        preferencias.Should().ContainSingle();
        await ConfirmarPagamento(factory, envios[0].PedidoId, "100001", 15);

        var novo = await Link(factory, c.ConversaId);
        var compartilhada = await Estado(client, novo);
        await Salvar(client, novo, compartilhada, compartilhada.Rascunho with { Itens = [new(c.CardapioItemId, 2, "sem granulado")] });
        await Etapa("preparando");
        await Etapa("pronto");
        (await SaldoEstoque()).Should().Be(4);
        var desatualizado = await client.PostAsJsonAsync($"{Rota(novo)}/confirmar", new { revisao = 1 });
        desatualizado.StatusCode.Should().Be(HttpStatusCode.Conflict, "o preparo mudou desde a seleção salva");
        var atual = await Estado(client, novo);
        atual.Rascunho.Itens.Single().Qtd.Should().Be(2, "a conferência preserva a seleção do cliente");
        atual.Rascunho.PedidoAtualizadoEm.Should().NotBe(compartilhada.Rascunho.PedidoAtualizadoEm);
        await Salvar(client, novo, atual, atual.Rascunho);
        var acrescimo = await Enviar(client, novo, 2);
        acrescimo.PedidoId.Should().Be(envios[0].PedidoId);
        acrescimo.Total.Should().Be(25);
        acrescimo.Saldo.Should().Be(10);
        (await Estado(client, novo)).Pedido!.Status.Should().Be("preparando");
        (await SaldoEstoque()).Should().Be(5, "a alteração retorna o estoque antigo antes de refazer o preparo");
        // #1717-7 (#1961): o primeiro link cobra o sinal da encomenda; o pagamento integral é aceito e o acréscimo cobra a diferença.
        preferencias.Values.Select(p => p.ValorTotal).Should().BeEquivalentTo([7.5m, 10m]);
        using (var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
        using (var stream = await client.GetAsync($"{Rota(novo)}/eventos", HttpCompletionOption.ResponseHeadersRead, prazo.Token))
        using (var leitor = new StreamReader(await stream.Content.ReadAsStreamAsync(prazo.Token)))
        {
            await leitor.ReadLineAsync(prazo.Token); await leitor.ReadLineAsync(prazo.Token);
            await ConfirmarPagamento(factory, acrescimo.PedidoId, "100002", 10);
            string? linha;
            do { linha = await leitor.ReadLineAsync(prazo.Token); } while (linha is not null && !linha.StartsWith("data:", StringComparison.Ordinal));
            linha.Should().NotBeNull();
            var aoVivo = JsonSerializer.Deserialize<ComandaClienteResult>(linha![5..], ComandaClienteUseCase.Json)!;
            aoVivo.Pedido!.TotalPago.Should().Be(25, "o segundo pagamento publica saldo novo após commit sem esperar o polling");
        }
        await Etapa("pronto");
        await Etapa("pronto");
        (await SaldoEstoque()).Should().Be(3, "o novo preparo baixa duas unidades uma única vez");
        (await Estado(client, token)).Pedido!.TotalPago.Should().Be(25);
        var consumido = await client.PutAsJsonAsync($"{Rota(token)}/rascunho", new { Revisao = 1, Rascunho = dados });
        consumido.StatusCode.Should().Be(HttpStatusCode.Gone);
        using var finalScope = factory.Services.CreateScope();
        var db = finalScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.Pedidos.CountAsync()).Should().Be(1);
        (await db.Set<PedidoPagamento>().CountAsync()).Should().Be(2);
        (await db.NotifEventos.AsNoTracking().ToListAsync()).Count(e => e.PayloadJson.Contains("pagamento:") && e.PayloadJson.Contains("100002"))
            .Should().Be(1, "pagamento complementar tem marco próprio e seu replay não enfileira outro aviso");

        async Task Etapa(string status)
        {
            using var scope = factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(EmpresaId);
            await scope.ServiceProvider.GetRequiredService<AtualizarStatusPedidoUseCase>().ExecuteAsync(
                new AtualizarStatusPedidoCommand(EmpresaId, envios[0].PedidoId, status, null, null, "web"));
        }
        async Task<decimal> SaldoEstoque()
        {
            using var scope = factory.Services.CreateScope();
            var dbEstoque = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            dbEstoque.SetMobileTenantContext(EmpresaId);
            return (await dbEstoque.Set<ItemEstoque>().Where(i => i.ProdutoId == produtoId).ToListAsync()).Sum(i => i.QuantidadeAtual.Value);
        }
    }

    [SkippableFact]
    public async Task Comanda_ReducaoEmCupomNaoDuplicaCreditoNemPermiteEstornoDoMesmoValor()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        await using var factory = CriarFactory(Provedor(preferencias), NivelAcesso.Admin);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 3)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, 1);
        await ConfirmarPagamento(factory, envio.PedidoId, "200001", 35);
        var ajuste = await Link(factory, c.ConversaId);
        estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], Acerto = "cupom" });
        (await Enviar(client, ajuste, 1)).Situacao.Should().Be("alteracao_aguardando_confirmacao");
        var confirmar = $"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar";
        for (var i = 0; i < 2; i++)
        {
            var resposta = await client.PostAsJsonAsync(confirmar, new { Aceitar = true });
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        }
        string codigo;
        (await Estado(client, ajuste)).Acerto!.Valor.Should().Be(20);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var cupom = await db.Set<CreditoClienteComanda>().SingleAsync();
            cupom.Saldo.Should().Be(20); codigo = cupom.Codigo;
            var pagamento = await db.Set<PedidoPagamento>().SingleAsync();
            var estorno = scope.ServiceProvider.GetRequiredService<IEstornosOnlineService>();
            var duplicar = () => estorno.ReservarAsync(new(EmpresaId, envio.PedidoId, Guid.NewGuid(), pagamento.Id, 20,
                "Não pode devolver crédito já emitido", UsuarioId, "Teste", NivelAcesso.Admin));
            await duplicar.Should().ThrowAsync<CobrancaPedidoConflitoException>();
            var creditos = scope.ServiceProvider.GetRequiredService<ICreditoClienteComandaRepository>();
            (await creditos.ObterComLockAsync(EmpresaId, Guid.NewGuid(), codigo, CancellationToken.None)).Should().BeNull();
            var pedido = await db.Pedidos.SingleAsync(); pedido.Status = "entregue"; await db.SaveChangesAsync();
        }
        var seguinte = await Link(factory, c.ConversaId);
        estado = await Estado(client, seguinte);
        estado.Pedido.Should().BeNull();
        await Salvar(client, seguinte, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId,
            DataEntrega = c.DataEntrega, CodigoCupom = codigo });
        var pagoComCupom = await Enviar(client, seguinte, 1);
        pagoComCupom.Saldo.Should().Be(0); pagoComCupom.LinkPagamento.Should().BeNull();
        preferencias.Should().ContainSingle("o segundo pedido foi coberto pelo cupom");
        (await Estado(client, seguinte)).Pedido!.TotalPago.Should().Be(15);
        (await Estado(client, seguinte)).Envio!.CupomAplicado!.Valor.Should().Be(15);
        (await Estado(client, seguinte)).Envio!.CupomAplicado!.SaldoCupom.Should().Be(5);
        (await Estado(client, ajuste)).Acerto!.SaldoCupom.Should().Be(5, "recibo antigo acompanha saldo atual após uso parcial");
        using var finalScope = factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await finalDb.Set<CreditoClienteComanda>().SingleAsync()).Saldo.Should().Be(5);
        (await finalDb.Set<PedidoPagamento>().SingleAsync()).Valor.Should().Be(35);
        (await finalDb.Set<UsoCreditoClienteComanda>().SingleAsync()).Valor.Should().Be(15);
        using var cancelarScope = factory.Services.CreateScope();
        var cancelar = cancelarScope.ServiceProvider.GetRequiredService<EasyStock.Application.UseCases.CancelarPedido.CancelarPedidoUseCase>();
        await cancelar.ExecuteAsync(new(EmpresaId, pagoComCupom.PedidoId, UsuarioId, "Teste", "Cancelamento do pedido com crédito", NivelSolicitante: NivelAcesso.Admin));
        var carteira = await cancelarScope.ServiceProvider.GetRequiredService<ICreditoClienteComandaRepository>().ListarAsync(EmpresaId, c.ClienteId, CancellationToken.None);
        carteira.Sum(credito => credito.Saldo).Should().Be(20, "o crédito usado no pedido cancelado volta à carteira");
        var consumirSaldo = await Link(factory, c.ConversaId); estado = await Estado(client, consumirSaldo);
        await Salvar(client, consumirSaldo, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)], JanelaId = c.JanelaId,
            DataEntrega = c.DataEntrega, CodigoCupom = codigo });
        var consumo = await Enviar(client, consumirSaldo, 1);
        consumo.CupomAplicado!.Valor.Should().Be(5); consumo.CupomAplicado.SaldoCupom.Should().Be(0);
        (await Estado(client, consumirSaldo)).Envio!.CupomAplicado!.SaldoCupom.Should().Be(0);
        (await Estado(client, ajuste)).Acerto!.SaldoCupom.Should().Be(0, "ausência na carteira não é usada para inferir saldo zero");
    }

    [SkippableFact]
    public async Task Comanda_ReducaoConfirmaEstornoParcialERepeticaoNaoDevolveDuasVezes()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        var mp = Provedor(preferencias);
        await using var factory = CriarFactory(mp, NivelAcesso.Admin);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory); var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 3)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, 1);
        mp.ConsultarPagamentoAsync("300001", Arg.Any<CancellationToken>()).Returns(new PagamentoMercadoPago(
            "300001", "approved", null, envio.PedidoId.ToString(), 35, DateTime.UtcNow, "pix", "bank_transfer"));
        mp.ListarEstornosAsync("300001", Arg.Any<CancellationToken>()).Returns(Array.Empty<EstornoMercadoPagoResult>());
        mp.EstornarAsync("300001", 20, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new EstornoMercadoPagoResult(Guid.NewGuid().ToString("N"), 20, "approved", "300001", DateTime.UtcNow),
            new EstornoMercadoPagoResult("900001", 20, "approved", "300001", DateTime.UtcNow));
        var ajuste = await Link(factory, c.ConversaId); estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], Acerto = "estorno" });
        await Enviar(client, ajuste, 1);
        await ConfirmarPagamento(factory, envio.PedidoId, "300001", 35);
        var confirmar = $"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar";
        var primeira = await client.PostAsJsonAsync(confirmar, new { Aceitar = true });
        primeira.StatusCode.Should().Be(HttpStatusCode.OK, await primeira.Content.ReadAsStringAsync());
        var pendente = await Estado(client, ajuste);
        pendente.Envio!.Situacao.Should().Be("estorno_pendente");
        pendente.Pedido!.TotalPago.Should().Be(35, "resposta inválida do provedor não registra devolução");
        Guid operacaoId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var operacao = await db.Set<PedidoEstornoOnline>().SingleAsync();
            operacaoId = operacao.Id;
            operacao.Situacao.Should().Be(PedidoEstornoOnline.Pendente);
            var avisos = await db.NotifEventos.Where(e => e.RefEntidadeId == envio.PedidoId)
                .Select(e => e.PayloadJson).ToListAsync();
            var payload = avisos.Single(p => p.Contains($"comanda:{estado.LinkId:N}:estorno_pendente"));
            using var aviso = JsonDocument.Parse(payload);
            aviso.RootElement.GetProperty("mensagem").GetString().Should()
                .Contain("Seu pedido foi atualizado. Total: R$ 15,00.")
                .And.Contain("Solicitamos a devolução de R$ 20,00 pelo Mercado Pago. Aguarde a confirmação.")
                .And.NotContain("foi devolvido");
            operacao.EstornoExternoId.Should().BeNull();
            operacao.MovimentoCaixaId.Should().BeNull();
            operacao.Detalhe.Should().Contain("sem confirmação válida");
        }
        for (var i = 0; i < 2; i++)
        {
            var resposta = await client.PostAsJsonAsync(confirmar, new { Aceitar = true });
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        }
        var final = await Estado(client, ajuste);
        final.Envio!.Situacao.Should().Be("estorno_confirmado"); final.Pedido!.TotalPago.Should().Be(15);
        await mp.Received(2).EstornarAsync("300001", 20, operacaoId.ToString(), Arg.Any<CancellationToken>());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var operacao = await db.Set<PedidoEstornoOnline>().SingleAsync();
            operacao.Id.Should().Be(operacaoId, "a retomada conserva a intenção original");
            operacao.Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
            operacao.EstornoExternoId.Should().Be("900001");
            (await db.Set<MovimentoCaixa>().CountAsync(m => m.Referencia == "900001" && m.Tipo == "saida")).Should().Be(1);
        }
        var conflitante = await Link(factory, c.ConversaId); estado = await Estado(client, conflitante);
        await Salvar(client, conflitante, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1, "nova observação do cliente")] });
        (await Enviar(client, conflitante, 1)).Situacao.Should().Be("alteracao_aguardando_confirmacao");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var pedido = await db.Pedidos.SingleAsync(p => p.Id == envio.PedidoId);
            pedido.Observacoes = "O atendimento modificou o conteúdo depois da proposta";
            await db.SaveChangesAsync();
        }
        var stale = await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar", new { Aceitar = true });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict, "pagamento isolado é aceito, mudança real do conteúdo permanece protegida");
    }

    [SkippableFact]
    public async Task Comanda_StreamEnxergaAlteracaoDoAtendenteSemReabrirOLink()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new())); using var client = factory.CreateClient();
        var c = await SemearAsync(factory); var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var resposta = await client.GetAsync($"{Rota(token)}/eventos", HttpCompletionOption.ResponseHeadersRead, prazo.Token);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using var leitor = new StreamReader(await resposta.Content.ReadAsStreamAsync(prazo.Token));
        (await leitor.ReadLineAsync(prazo.Token)).Should().Be("event: comanda");
        (await leitor.ReadLineAsync(prazo.Token)).Should().Contain("\"revisao\":0");
        var rascunho = estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)], Observacoes = "Anotação do atendimento" };
        var salvo = await client.PutAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/rascunho",
            new { Revisao = 0, Rascunho = rascunho }, prazo.Token);
        salvo.StatusCode.Should().Be(HttpStatusCode.OK, await salvo.Content.ReadAsStringAsync(prazo.Token));
        string? linha;
        do { linha = await leitor.ReadLineAsync(prazo.Token); } while (linha is not null && !linha.StartsWith("data:", StringComparison.Ordinal));
        linha.Should().Contain("\"revisao\":1");
        (await Estado(client, token)).Rascunho.Observacoes.Should().Be("Anotação do atendimento");
    }

    // #1686-6: com a alteração da comanda aguardando a aprovação, a atendente troca a janela do pedido. A troca grava
    // AlteradoEm e mudava o resumo do pedido (a data entrava nele), e o aceite respondia 409 a uma alteração de itens
    // que não conflita com a data.
    [SkippableFact]
    public async Task Comanda_ReagendarComAlteracaoPendenteNaoTravaAConfirmacao()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, 1);
        var ajuste = await Link(factory, c.ConversaId);
        estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1, "sem granulado")] });
        (await Enviar(client, ajuste, 1)).Situacao.Should().Be("alteracao_aguardando_confirmacao");

        var novaData = c.DataEntrega.AddDays(7);
        var troca = await client.PatchAsJsonAsync($"/api/pedidos/{envio.PedidoId}/janela", new { c.JanelaId, Data = novaData });
        troca.StatusCode.Should().Be(HttpStatusCode.OK, await troca.Content.ReadAsStringAsync());

        var confirmar = await client.PostAsJsonAsync(
            $"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar", new { Aceitar = true });

        confirmar.StatusCode.Should().Be(HttpStatusCode.OK, await confirmar.Content.ReadAsStringAsync());
        var final = await Estado(client, ajuste);
        final.Pedido!.Itens.Should().ContainSingle().Which.Observacao.Should().Be("sem granulado");
        final.Pedido.Janela!.Data.Should().Be(novaData, "a alteração confirmada mantém a data reagendada");
    }

    // #1686-2: o link já usado mostrava o pedido cancelado (ou entregue) como o pedido da comanda, com o saldo e o link de
    // pagamento dele; a Ficha dizia "Mais a pagar" de pedido cancelado.
    [SkippableTheory]
    [InlineData("cancelado")]
    [InlineData("entregue")]
    public async Task Comanda_LinkUsadoNaoMostraPedidoEncerradoNemSaldoDele(string encerramento)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()), NivelAcesso.Admin);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, 1);
        envio.Saldo.Should().BePositive();
        envio.LinkPagamento.Should().NotBeNull();
        using (var scope = factory.Services.CreateScope())
        {
            if (encerramento == "cancelado")
                await scope.ServiceProvider.GetRequiredService<EasyStock.Application.UseCases.CancelarPedido.CancelarPedidoUseCase>().ExecuteAsync(
                    new(EmpresaId, envio.PedidoId, UsuarioId, "Teste", "Cliente desistiu", NivelSolicitante: NivelAcesso.Admin));
            else
            {
                var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                db.SetMobileTenantContext(EmpresaId);
                (await db.Pedidos.SingleAsync(p => p.Id == envio.PedidoId)).Status = encerramento;
                await db.SaveChangesAsync();
            }
        }

        var usado = await Estado(client, token);
        using var listaJson = JsonDocument.Parse(await client.GetStringAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente"));
        var lista = listaJson.RootElement.GetProperty("data").Deserialize<List<ComandaClienteResult>>(ComandaClienteUseCase.Json)!;

        foreach (var retrato in new[] { usado, lista.Single(l => l.LinkId == usado.LinkId) })
        {
            retrato.Pedido.Should().BeNull("o pedido encerrado não é mais o pedido da comanda");
            retrato.PedidoEncerrado.Should().Be(encerramento);
            retrato.Envio!.Saldo.Should().Be(0, "o saldo não vem do pedido encerrado");
            retrato.Envio.LinkPagamento.Should().BeNull("o link de pagamento do pedido encerrado não é oferecido");
        }
        var reenvio = await Enviar(client, token, 1);
        reenvio.Saldo.Should().Be(0, "o reenvio do link usado também não oferece cobrança do pedido encerrado");
        reenvio.LinkPagamento.Should().BeNull();
    }
}
