using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task Comanda1712_OperadorConfirmaPrimeiroPedidoDaMesmaSelecaoJaComCobranca()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        var rota = $"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar-pedido";
        (await client.PostAsJsonAsync(rota, new { estado.Revisao })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)], Observacoes = "Confirmado na conversa" });
        estado = await Estado(client, token);
        (await client.PostAsJsonAsync(rota, new { estado.Revisao })).StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a confirmação assistida não contorna a janela obrigatória");
        await Salvar(client, token, estado, estado.Rascunho with { JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        (await client.PostAsJsonAsync(rota, new { estado.Revisao })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        estado = await Estado(client, token);
        var semFrete = await client.PostAsJsonAsync(rota, new { estado.Revisao });
        semFrete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await semFrete.Content.ReadAsStringAsync()).Should().Contain("Combine o frete");
        var baseOperador = $"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}";
        (await client.PostAsJsonAsync(baseOperador + "/encaminhar-entrega", new { estado.Revisao })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(baseOperador + "/combinar-frete", new { estado.Revisao, Valor = 5m })).StatusCode.Should().Be(HttpStatusCode.OK);
        var respostas = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(rota, new { estado.Revisao })));
        var pedidos = new List<Guid>();
        foreach (var resposta in respostas)
        {
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
            var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
            pedidos.Add(corpo.GetProperty("data").GetProperty("pedidoId").GetGuid());
            corpo.GetProperty("data").GetProperty("linkPagamento").ValueKind.Should().Be(JsonValueKind.String,
                "o pedido confirmado pela atendente nasce com cobrança (#1726)");
        }
        pedidos.Distinct().Should().ContainSingle();
        var final = await Estado(client, token);
        final.Pedido!.PedidoId.Should().Be(pedidos[0]);
        final.Pedido.Total.Should().Be(25m);
        final.Pedido.Itens.Should().ContainSingle().Which.Quantidade.Should().Be(2);
        final.PodeEditar.Should().BeFalse();
        (await client.PostAsJsonAsync(rota, new { estado.Revisao })).StatusCode.Should().Be(HttpStatusCode.OK);
        var ajuste = await Link(factory, c.ConversaId);
        var selecaoAjuste = await Estado(client, ajuste);
        (await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{selecaoAjuste.LinkId}/confirmar-pedido",
            new { selecaoAjuste.Revisao })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.Pedidos.CountAsync()).Should().Be(1);
        (await db.CobrancasPedido.CountAsync()).Should().Be(1, "sem cobrança a varredura de abandonados cancelava o pedido em silêncio (#1726)");
        (await db.AtendimentoMensagens.CountAsync(x => x.ConversaId == c.ConversaId && x.Texto!.StartsWith("Pedido confirmado pelo atendimento"))).Should().Be(1);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Comanda1712_EncaminhaLeadSemJanelaSemCobrarEConfereRevisao(bool peloAtendimento)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        Guid conversaId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var lead = Conversa.Abrir(EmpresaId, "5511912345601", DateTime.UtcNow, "Lead sem janela");
            db.AtendimentoConversas.Add(lead);
            await db.SaveChangesAsync();
            conversaId = lead.Id;
        }
        var token = await Link(factory, conversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, new([new(c.CardapioItemId, 2)],
            Endereco: new("20040002", "Rua distante", "42", "Centro", "Rio de Janeiro", "RJ"),
            Nome: "Lead sem janela", Telefone: "11912345601"));
        estado = await Estado(client, token);
        var rota = peloAtendimento
            ? $"/api/atendimento/conversas/{conversaId}/comandas-cliente/{estado.LinkId}/encaminhar-entrega"
            : $"{Rota(token)}/encaminhar-entrega";
        var respostas = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(rota, new { estado.Revisao })));
        foreach (var resposta in respostas)
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        var final = await Estado(client, token);
        final.EntregaACombinar.Should().BeTrue();
        final.PrecisaCadastro.Should().BeFalse();
        final.Pedido.Should().BeNull();
        final.Rascunho.JanelaId.Should().BeNull();
        final.Rascunho.DataEntrega.Should().BeNull();
        final.Rascunho.Itens.Should().ContainSingle().Which.Qtd.Should().Be(2);
        final.Rascunho.Endereco!.Cep.Should().Be("20040002");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            (await db.AtendimentoConversas.SingleAsync(x => x.Id == conversaId)).Situacao.Should().Be(SituacaoConversa.Assumida);
            (await db.AtendimentoMensagens.CountAsync(x => x.ConversaId == conversaId && x.Texto!.StartsWith("Cliente enviou a comanda"))).Should().Be(1);
            (await db.Pedidos.CountAsync()).Should().Be(0);
            (await db.CobrancasPedido.CountAsync()).Should().Be(0);
        }
        await Salvar(client, token, final, final.Rascunho with { Observacoes = "Conferir acesso" });
        (await client.PostAsJsonAsync(rota, new { final.Revisao })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Estado(client, token)).EntregaACombinar.Should().BeFalse();
    }

    [SkippableFact]
    public async Task Comanda1839_LeadFinalizaSemEnderecoEAtendentePedeNaConversa()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        Guid conversaId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var lead = Conversa.Abrir(EmpresaId, "5511912345602", DateTime.UtcNow, "Lead sem endereço");
            db.AtendimentoConversas.Add(lead);
            await db.SaveChangesAsync();
            conversaId = lead.Id;
        }
        var token = await Link(factory, conversaId);
        var estado = await Estado(client, token);
        // O site manda o endereço em branco quando a cliente não preenche: vale como não informado.
        await Salvar(client, token, estado, new([new(c.CardapioItemId, 1)],
            Endereco: new("", "", "", "", "", "", ""), Nome: "Lead sem endereço", Telefone: "11912345602"));
        estado = await Estado(client, token);
        estado.Rascunho.Endereco.Should().BeNull();
        (await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { estado.Revisao })).StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "o pedido com cobrança continua exigindo o endereço da entrega");
        var resposta = await client.PostAsJsonAsync($"{Rota(token)}/encaminhar-entrega", new { estado.Revisao });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        var final = await Estado(client, token);
        final.EntregaACombinar.Should().BeTrue();
        final.PrecisaCadastro.Should().BeFalse();
        final.Pedido.Should().BeNull();
        final.Rascunho.Endereco.Should().BeNull();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var conversa = await db.AtendimentoConversas.SingleAsync(x => x.Id == conversaId);
            conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
            (await db.Clientes.Include(x => x.Enderecos).SingleAsync(x => x.Id == conversa.ClienteId)).Enderecos.Should().BeEmpty();
            (await db.AtendimentoMensagens.CountAsync(x => x.ConversaId == conversaId && x.Texto!.Contains("Endereço pendente"))).Should().Be(1);
            (await db.Pedidos.CountAsync()).Should().Be(0);
            (await db.CobrancasPedido.CountAsync()).Should().Be(0);
        }
        // A atendente registra o endereço pela Ficha e a comanda passa a mostrá-lo.
        (await client.PostAsJsonAsync($"/api/atendimento/conversas/{conversaId}/cliente", new
        {
            Endereco = new { Cep = "01310100", Logradouro = "Av. Paulista", Numero = "900", Bairro = "Bela Vista", Cidade = "São Paulo", Uf = "SP" },
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Estado(client, token)).Rascunho.Endereco!.Numero.Should().Be("900");
    }

    [SkippableFact]
    public async Task Comanda1712_AjudaSemRascunhoEscalaUmaVezNotificaEPreservaEdicaoConcorrente()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        Guid conversaId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var lead = Conversa.Abrir(EmpresaId, "5511912345602", DateTime.UtcNow, "Preciso de ajuda");
            db.AtendimentoConversas.Add(lead);
            await db.SaveChangesAsync();
            conversaId = lead.Id;
        }
        var token = await Link(factory, conversaId);
        var inicial = await Estado(client, token);
        inicial.Rascunho.Itens.Should().BeEmpty();
        inicial.Rascunho.Endereco.Should().BeNull();
        inicial.Pedido.Should().BeNull();
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var stream = await client.GetAsync($"{Rota(token)}/eventos", HttpCompletionOption.ResponseHeadersRead, prazo.Token);
        using var leitor = new StreamReader(await stream.Content.ReadAsStreamAsync(prazo.Token));
        await leitor.ReadLineAsync(prazo.Token); await leitor.ReadLineAsync(prazo.Token);
        var respostas = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsync($"{Rota(token)}/pedir-ajuda", null)));
        foreach (var resposta in respostas)
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        string? linha;
        do { linha = await leitor.ReadLineAsync(prazo.Token); } while (linha is not null && !linha.StartsWith("data:", StringComparison.Ordinal));
        var evento = JsonSerializer.Deserialize<ComandaClienteResult>(linha![5..], ComandaClienteUseCase.Json)!;
        evento.AjudaSolicitada.Should().BeTrue();
        var ajuda = await Estado(client, token);
        ajuda.AjudaSolicitada.Should().BeTrue();
        ajuda.AjudaSolicitadaEm.Should().NotBeNull();
        ajuda.PrecisaCadastro.Should().BeTrue();
        ajuda.Revisao.Should().Be(inicial.Revisao);
        ajuda.Rascunho.Should().BeEquivalentTo(inicial.Rascunho);
        var rascunho = ajuda.Rascunho with { Itens = [new(c.CardapioItemId, 1)], Observacoes = "Ainda escolhendo" };
        var concorrentes = await Task.WhenAll(client.PutAsJsonAsync($"{Rota(token)}/rascunho", new { ajuda.Revisao, rascunho }),
            client.PostAsync($"{Rota(token)}/pedir-ajuda", null));
        foreach (var resposta in concorrentes)
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        var editado = await Estado(client, token);
        editado.Rascunho.Observacoes.Should().Be("Ainda escolhendo");
        editado.Rascunho.Itens.Should().ContainSingle();
        editado.AjudaSolicitadaEm.Should().Be(ajuda.AjudaSolicitadaEm);
        var operador = await client.GetFromJsonAsync<JsonElement>($"/api/atendimento/conversas/{conversaId}/comandas-cliente");
        operador.GetProperty("data").EnumerateArray().Single(x => x.GetProperty("linkId").GetGuid() == ajuda.LinkId)
            .GetProperty("ajudaSolicitada").GetBoolean().Should().BeTrue();
        using var verificacao = factory.Services.CreateScope();
        var banco = verificacao.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var conversa = await banco.AtendimentoConversas.SingleAsync(x => x.Id == conversaId);
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        conversa.MotivoEscalada.Should().Contain("pediu ajuda");
        conversa.ClienteId.Should().BeNull();
        (await banco.AtendimentoMensagens.CountAsync(x => x.ConversaId == conversaId && x.Texto!.StartsWith("Cliente pediu ajuda"))).Should().Be(1);
        (await banco.Pedidos.CountAsync()).Should().Be(0);
        (await banco.CobrancasPedido.CountAsync()).Should().Be(0);
        (await client.PostAsync($"/api/atendimento/conversas/{conversaId}/liberar-automatico", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var retomada = await Estado(client, token);
        retomada.AjudaSolicitada.Should().BeFalse();
        retomada.AjudaSolicitadaEm.Should().BeNull();
        (await client.PostAsync($"/api/atendimento/conversas/{conversaId}/assumir", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var reassumida = await Estado(client, token);
        reassumida.AjudaSolicitada.Should().BeFalse("assumir por conta própria não reativa um pedido de ajuda anterior");
        reassumida.AjudaSolicitadaEm.Should().BeNull();
        var novas = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsync($"{Rota(token)}/pedir-ajuda", null)));
        foreach (var resposta in novas)
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        var novaAjuda = await Estado(client, token);
        novaAjuda.AjudaSolicitada.Should().BeTrue();
        novaAjuda.AjudaSolicitadaEm.Should().BeAfter(ajuda.AjudaSolicitadaEm!.Value);
        novaAjuda.Rascunho.Should().BeEquivalentTo(editado.Rascunho);
        await banco.Entry(conversa).ReloadAsync();
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        (await banco.AtendimentoMensagens.CountAsync(x => x.ConversaId == conversaId && x.Texto!.StartsWith("Cliente pediu ajuda"))).Should().Be(2);
    }

    [SkippableFact]
    public async Task Comanda1712_TokenExpiradoInvalidoEEmpresaAlheiaNaoAlteramOutraConversa()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var empresaB = Guid.NewGuid();
        var tokenB = Guid.NewGuid().ToString("N");
        Guid conversaB, linkB;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaB);
            db.Empresas.Add(new Empresa { Id = empresaB, Nome = "Outra empresa", Documento = empresaB.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            var conversa = Conversa.Abrir(empresaB, "5511912345603", DateTime.UtcNow, "Outro cliente");
            var link = LinkCardapioConversa.Gerar(empresaB, conversa.Id, AcessoChatSite.HashDoToken(tokenB), DateTime.UtcNow);
            db.AtendimentoConversas.Add(conversa); db.LinksCardapioConversa.Add(link);
            await db.SaveChangesAsync();
            conversaB = conversa.Id; linkB = link.Id;
        }
        (await client.PostAsJsonAsync($"/api/atendimento/conversas/{conversaB}/comandas-cliente/{linkB}/encaminhar-entrega", new { Revisao = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{linkB}/encaminhar-entrega", new { Revisao = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.PostAsJsonAsync($"/api/atendimento/conversas/{conversaB}/comandas-cliente/{linkB}/confirmar-pedido", new { Revisao = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{linkB}/confirmar-pedido", new { Revisao = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.PostAsync($"{Rota(tokenB)}/pedir-ajuda", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Estado(client, tokenB)).ConversaId.Should().Be(conversaB);
        (await Estado(client, tokenB)).AjudaSolicitada.Should().BeTrue();
        (await Estado(client, token)).AjudaSolicitada.Should().BeFalse();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var link = await db.LinksCardapioConversa.SingleAsync(x => x.ConversaId == c.ConversaId);
            link.Expirar(DateTime.UtcNow.AddSeconds(-1));
            await db.SaveChangesAsync();
        }
        foreach (var indisponivel in new[] { token, Guid.NewGuid().ToString("N") })
        {
            (await client.PostAsync($"{Rota(indisponivel)}/pedir-ajuda", null)).StatusCode.Should().Be(HttpStatusCode.Gone);
            (await client.PostAsJsonAsync($"{Rota(indisponivel)}/encaminhar-entrega", new { Revisao = 0 })).StatusCode.Should().Be(HttpStatusCode.Gone);
        }
    }
}
