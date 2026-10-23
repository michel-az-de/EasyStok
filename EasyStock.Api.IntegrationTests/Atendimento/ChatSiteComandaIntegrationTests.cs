using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatSite_Comanda_PedidoPublicoAtomicoComLinkOuSinalManual(bool manual)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        var mp = Provedor(preferencias);
        if (manual) mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MercadoPagoNaoConfiguradoException());
        await using var factory = CriarFactory(mp);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory, "chat-comanda");
        var conversaId = await PrepararChatComanda(factory, client, c.ClienteId);
        var token = await Link(factory, conversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        estado = await Estado(client, token);
        (await PublicasChatComanda(client)).Should().BeEmpty("autosave não produz confirmação de pedido");

        var envios = await Task.WhenAll(Enviar(client, token, estado.Revisao), Enviar(client, token, estado.Revisao));
        envios.Select(e => e.PedidoId).Distinct().Should().ContainSingle();
        await Enviar(client, token, estado.Revisao);
        var mensagens = await PublicasChatComanda(client);
        await GuardarProvaChatComanda(client, manual ? "manual" : "pedido");
        var texto = mensagens.Should().ContainSingle().Which.GetProperty("texto").GetString()!;
        texto.Should().Contain("Recebemos seu pedido").And.Contain("15,00");
        texto.Should().NotContain("pagamento confirmado").And.NotContain("pedido aprovado");
        if (manual) texto.Should().Contain("Sinal a receber").And.Contain("7,50").And.Contain("Restante na entrega");
        else texto.Should().Contain(envios[0].LinkPagamento!);
        await ConferirStreamChatComanda(client, texto);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            (await db.Pedidos.CountAsync()).Should().Be(1);
            (await db.CobrancasPedido.CountAsync()).Should().Be(1);
            (await db.AtendimentoMensagens.AnyAsync(m => m.ConversaId == conversaId && m.ExternoId == null
                && m.Texto!.Contains("Pedido montado pelo cliente"))).Should().BeTrue("a nota privada permanece");
            (await db.AtendimentoMensagens.CountAsync(m => m.ExternoId!.StartsWith("chatsite:comanda:pedido:"))).Should().Be(1);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var empresa = Guid.NewGuid();
            db.SetMobileTenantContext(empresa);
            db.Empresas.Add(new Empresa { Id = empresa, Nome = "Outra loja", Documento = empresa.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            var loja = EasyStock.Domain.Entities.Storefront.Storefront.Criar(empresa, "outra-loja", "Outra loja", 0m);
            loja.Ativar(); db.Storefronts.Add(loja);
            db.TenantFeatureFlags.AddRange(
                TenantFeatureFlag.Criar(empresa, FeatureCatalogo.ModuloAtendimento, true, "teste@local.invalid"),
                TenantFeatureFlag.Criar(empresa, FeatureCatalogo.CanalChatSite, true, "teste@local.invalid"));
            await db.SaveChangesAsync();
        }
        (await client.GetAsync("/api/public/chat/outra-loja/mensagens")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var semToken = factory.CreateClient();
        (await semToken.GetAsync("/api/public/chat/chat-comanda/mensagens")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A falha da saída pública deve desfazer também pedido, cobrança e consumo do link. Desde o #1961 a cobrança sai
        // depois do commit do pedido (a conversa não fica travada durante o provedor): desfazer é cancelar o pedido e a
        // cobrança com o motivo e devolver o link; nenhum pedido ou cobrança ATIVO fica para trás.
        Guid outraConversa;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var nova = EasyStock.Domain.Entities.Atendimento.Conversa.Abrir(EmpresaId, "chat-rollback", DateTime.UtcNow,
                clienteId: c.ClienteId, canal: CanalConversa.ChatSite);
            db.AtendimentoConversas.Add(nova);
            await db.SaveChangesAsync();
            outraConversa = nova.Id;
        }
        var segundo = await Link(factory, outraConversa);
        estado = await Estado(client, segundo);
        await Salvar(client, segundo, estado, estado.Rascunho with
        { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        estado = await Estado(client, segundo);
        await FalharAvisoChatComanda(factory, true);
        (await client.PostAsJsonAsync($"{Rota(segundo)}/confirmar", new { estado.Revisao })).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            (await db.Pedidos.CountAsync(p => p.Status != "cancelado")).Should().Be(1);
            (await db.CobrancasPedido.CountAsync(x => x.Status != EasyStock.Domain.Enums.Pagamentos.StatusCobrancaPedido.Cancelada)).Should().Be(1);
            (await db.Pedidos.Where(p => p.Status == "cancelado").SelectMany(p => p.Eventos)
                .AnyAsync(e => e.Detalhes == EasyStock.Application.UseCases.Atendimento.Comanda.ComandaClienteUseCase.MotivoConfirmacaoNaoRegistrada))
                .Should().BeTrue("o pedido desfeito diz o motivo");
            (await db.LinksCardapioConversa.SingleAsync(l => l.Id == estado.LinkId)).UsadoEm.Should().BeNull();
        }
        await FalharAvisoChatComanda(factory, false);

        var waToken = await Link(factory, c.ConversaId);
        var waEstado = await Estado(client, waToken);
        await Salvar(client, waToken, waEstado, waEstado.Rascunho with
        { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        await Enviar(client, waToken, (await Estado(client, waToken)).Revisao);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            (await db.AtendimentoMensagens.AnyAsync(m => m.ConversaId == c.ConversaId
                && m.ExternoId!.StartsWith("chatsite:comanda:"))).Should().BeFalse("WhatsApp fica fora desta onda");
            db.AtendimentoMensagens.Add(EasyStock.Domain.Entities.Atendimento.Mensagem.Saida(EmpresaId, conversaId,
                AutorMensagem.Dona, DateTime.UtcNow.AddSeconds(1), TipoConteudoMensagem.Texto,
                "A equipe respondeu", "chatsite:comanda:humano"));
            await db.SaveChangesAsync();
        }
        using var inboxHumano = JsonDocument.Parse(await client.GetStringAsync("/api/atendimento/conversas"));
        var respostaHumana = inboxHumano.RootElement.GetProperty("data").EnumerateArray().Single(resumoConversa => resumoConversa.GetProperty("id").GetGuid() == conversaId);
        respostaHumana.GetProperty("aguardaResposta").GetBoolean().Should().BeFalse("prefixo não apaga resposta humana");
        respostaHumana.GetProperty("ultimaMensagemTexto").GetString().Should().Be("A equipe respondeu");
    }

    [SkippableFact]
    public async Task ChatSite_Comanda_FretePublicoAtomicoSemDuplicarOuLiberarAcesso()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory, "chat-comanda");
        var conversaId = await PrepararChatComanda(factory, client, c.ClienteId);
        var token = await Link(factory, conversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        { Itens = [new(c.CardapioItemId, 2)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega,
            Endereco = new("20040002", "Rua A", "10", "Centro", "Rio de Janeiro", "RJ") });
        estado = await Estado(client, token);
        var encaminhada = await client.PostAsJsonAsync($"{Rota(token)}/encaminhar-entrega", new { estado.Revisao });
        encaminhada.StatusCode.Should().Be(HttpStatusCode.OK, await encaminhada.Content.ReadAsStringAsync());
        var rota = $"/api/atendimento/conversas/{conversaId}/comandas-cliente/{estado.LinkId}/combinar-frete";
        await FalharAvisoChatComanda(factory, true);
        (await client.PostAsJsonAsync(rota, new { estado.Revisao, Valor = 13.5m })).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await Estado(client, token)).FreteCombinado.Should().BeNull();
        await FalharAvisoChatComanda(factory, false);

        await using var negada = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { s.RemoveAll<ICurrentUserAccessor>(); s.AddSingleton<ICurrentUserAccessor>(new ChatComandaUsuario(EmpresaId, false)); }));
        using var semPermissao = negada.CreateClient();
        (await semPermissao.PostAsJsonAsync(rota, new { estado.Revisao, Valor = 13.5m })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var outroTenant = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        { s.RemoveAll<ICurrentUserAccessor>(); s.AddSingleton<ICurrentUserAccessor>(new ChatComandaUsuario(Guid.NewGuid(), true)); }));
        using var outro = outroTenant.CreateClient();
        (await outro.PostAsJsonAsync(rota, new { estado.Revisao, Valor = 13.5m })).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await PublicasChatComanda(client)).Should().BeEmpty();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.PostAsJsonAsync(rota, new { estado.Revisao, Valor = 13.5m })));
        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await client.PostAsJsonAsync(rota, new { estado.Revisao, Valor = 13.5m })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(rota, new { Revisao = estado.Revisao - 1, Valor = 13.5m })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var texto = (await PublicasChatComanda(client)).Should().ContainSingle().Which.GetProperty("texto").GetString()!;
        texto.Should().Contain("13,50").And.Contain("confirme o total na comanda").And.NotContain("pago");
        await GuardarProvaChatComanda(client, "frete");
        await ConferirStreamChatComanda(client, texto);
        using var verificacao = factory.Services.CreateScope();
        var banco = verificacao.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await banco.Pedidos.CountAsync()).Should().Be(0);
        (await banco.CobrancasPedido.CountAsync()).Should().Be(0);
        (await banco.AtendimentoMensagens.CountAsync(m => m.ConversaId == conversaId && m.ExternoId == null && m.Texto!.StartsWith("Frete de"))).Should().Be(1);
    }

    private static async Task<Guid> PrepararChatComanda(WebApplicationFactory<Program> factory, HttpClient client, Guid clienteId)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.TenantFeatureFlags.AddRange(
                TenantFeatureFlag.Criar(EmpresaId, FeatureCatalogo.ModuloAtendimento, true, "teste@local.invalid"),
                TenantFeatureFlag.Criar(EmpresaId, FeatureCatalogo.CanalChatSite, true, "teste@local.invalid"));
            await db.SaveChangesAsync();
        }
        var abrir = await client.PostAsync("/api/public/chat/chat-comanda/sessoes", null);
        abrir.StatusCode.Should().Be(HttpStatusCode.OK);
        using var sessao = JsonDocument.Parse(await abrir.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-Chat-Token", sessao.RootElement.GetProperty("data").GetProperty("token").GetString());
        (await client.PostAsJsonAsync("/api/public/chat/chat-comanda/mensagens", new { texto = "Quero montar meu pedido" })).StatusCode.Should().Be(HttpStatusCode.OK);
        using var vinculo = factory.Services.CreateScope();
        var banco = vinculo.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var conversa = await banco.AtendimentoConversas.SingleAsync(c => c.Canal == CanalConversa.ChatSite);
        conversa.VincularCliente(clienteId);
        await banco.SaveChangesAsync();
        return conversa.Id;
    }

    private static async Task<JsonElement[]> PublicasChatComanda(HttpClient client)
    {
        var resposta = await client.GetAsync("/api/public/chat/chat-comanda/mensagens");
        (await resposta.Content.ReadAsStringAsync()).Should().NotContain("Pedido montado pelo cliente").And.NotContain("Frete de").And.NotContain("Frete pendente de decisão humana");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using var lista = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return lista.RootElement.GetProperty("data").EnumerateArray()
            .Where(m => !m.GetProperty("doVisitante").GetBoolean() && m.GetProperty("texto").GetString() != ConfirmacaoChat)
            .Select(m => m.Clone()).ToArray();
    }

    private static async Task GuardarProvaChatComanda(HttpClient client, string marco)
    {
        var pasta = Environment.GetEnvironmentVariable("EVIDENCIAS_CHAT_COMANDA");
        if (pasta is { Length: > 0 }) Directory.CreateDirectory(pasta);
        var resposta = await client.GetAsync("/api/public/chat/chat-comanda/mensagens");
        if (pasta is { Length: > 0 }) await File.WriteAllTextAsync(Path.Combine(pasta, $"{marco}.json"), await resposta.Content.ReadAsStringAsync());
        var inbox = await client.GetAsync("/api/atendimento/conversas");
        if (pasta is { Length: > 0 }) await File.WriteAllTextAsync(Path.Combine(pasta, $"{marco}-inbox.json"), await inbox.Content.ReadAsStringAsync());
        using var resumo = JsonDocument.Parse(await inbox.Content.ReadAsStringAsync());
        var chat = resumo.RootElement.GetProperty("data").EnumerateArray().Single(c => c.GetProperty("canal").GetString() == "ChatSite");
        chat.GetProperty("aguardaResposta").GetBoolean().Should().BeTrue("aviso de comanda não encerra espera humana");
        chat.GetProperty("ultimaMensagemTexto").GetString().Should().NotContain("Recebemos seu pedido").And.NotContain("O frete combinado é");
        var detalhes = await client.GetAsync($"/api/atendimento/conversas/{chat.GetProperty("id").GetGuid()}/mensagens");
        if (pasta is { Length: > 0 }) await File.WriteAllTextAsync(Path.Combine(pasta, $"{marco}-console.json"), await detalhes.Content.ReadAsStringAsync());
    }

    private static async Task ConferirStreamChatComanda(HttpClient client, string texto)
    {
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var resposta = await client.GetAsync("/api/public/chat/chat-comanda/stream", HttpCompletionOption.ResponseHeadersRead, prazo.Token);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using var leitor = new StreamReader(await resposta.Content.ReadAsStreamAsync(prazo.Token));
        for (var i = 0; i < 20; i++)
        {
            var linha = await leitor.ReadLineAsync(prazo.Token);
            if (linha?.StartsWith("data: ", StringComparison.Ordinal) != true) continue;
            using var evento = JsonDocument.Parse(linha[6..]);
            if (evento.RootElement.GetProperty("texto").GetString() == texto) return;
        }
        Assert.Fail("SSE não entregou a confirmação pública persistida.");
    }

    private static async Task FalharAvisoChatComanda(WebApplicationFactory<Program> factory, bool ligar)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        await db.Database.ExecuteSqlRawAsync(ligar ? """
            CREATE FUNCTION recusar_aviso_comanda_teste() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW."ExternoId" LIKE 'chatsite:comanda:%' THEN RAISE EXCEPTION 'falha controlada no aviso público'; END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER recusar_aviso_comanda_teste BEFORE INSERT ON atendimento_mensagens
              FOR EACH ROW EXECUTE FUNCTION recusar_aviso_comanda_teste();
            """ : "DROP TRIGGER recusar_aviso_comanda_teste ON atendimento_mensagens; DROP FUNCTION recusar_aviso_comanda_teste();");
    }

    private sealed class ChatComandaUsuario(Guid empresa, bool permitido) : ICurrentUserAccessor
    {
        public Guid EmpresaId => empresa;
        public bool IsAuthenticated => true;
        public Guid UsuarioId => PedidoConversaConcorrenciaTests.UsuarioId;
        public NivelAcesso Nivel => NivelAcesso.Operador;
        public bool TemPermissao(Permissao permissao) => permitido;
    }
}
