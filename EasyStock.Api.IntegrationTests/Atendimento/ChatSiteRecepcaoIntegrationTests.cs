using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.UseCases.FeatureFlags;
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
    private const string ConfirmacaoChat = "Recebemos sua mensagem. A equipe da loja responde por aqui.";

    [SkippableFact]
    public async Task ChatSite_FilaHumana_ConfirmaUmaVezMesmoComEntradasConcorrentes()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        var slug = "chat-recepcao-1742";
        await SemearAsync(factory, slug);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            db.TenantFeatureFlags.AddRange(
                TenantFeatureFlag.Criar(EmpresaId, FeatureCatalogo.ModuloAtendimento, true, "teste@local.invalid"),
                TenantFeatureFlag.Criar(EmpresaId, FeatureCatalogo.CanalChatSite, true, "teste@local.invalid"));
            await db.SaveChangesAsync();
        }
        var abrir = await client.PostAsync($"/api/public/chat/{slug}/sessoes", null);
        abrir.StatusCode.Should().Be(HttpStatusCode.OK);
        using var aberta = JsonDocument.Parse(await abrir.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-Chat-Token", aberta.RootElement.GetProperty("data").GetProperty("token").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION recusar_confirmacao_chat_teste() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."ExternoId" LIKE 'chatsite:recepcao:%' THEN RAISE EXCEPTION 'falha controlada de confirmação'; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER recusar_confirmacao_chat_teste BEFORE INSERT ON atendimento_mensagens
                  FOR EACH ROW EXECUTE FUNCTION recusar_confirmacao_chat_teste();
                """);
        }
        (await client.PostAsJsonAsync($"/api/public/chat/{slug}/mensagens", new { texto = "Não pode ficar pela metade" })).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            (await db.AtendimentoConversas.AnyAsync(c => c.Canal == CanalConversa.ChatSite)).Should().BeFalse();
            (await db.AtendimentoMensagens.AnyAsync(m => m.Texto == "Não pode ficar pela metade")).Should().BeFalse();
            (await db.SessoesChatSite.SingleAsync()).ConversaId.Should().BeNull();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER recusar_confirmacao_chat_teste ON atendimento_mensagens; DROP FUNCTION recusar_confirmacao_chat_teste();");
        }

        var respostas = await Task.WhenAll(Enumerable.Range(0, 3).Select(i =>
            client.PostAsJsonAsync($"/api/public/chat/{slug}/mensagens", new { texto = $"Dúvida {i + 1}" })));
        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var listar = await client.GetAsync($"/api/public/chat/{slug}/mensagens");
        listar.StatusCode.Should().Be(HttpStatusCode.OK);
        using var historico = JsonDocument.Parse(await listar.Content.ReadAsStringAsync());
        var mensagens = historico.RootElement.GetProperty("data").EnumerateArray().ToArray();
        if (Environment.GetEnvironmentVariable("EVIDENCIAS_CHAT_RECEPCAO") is { Length: > 0 } pasta)
        {
            Directory.CreateDirectory(pasta);
            await File.WriteAllTextAsync(Path.Combine(pasta, "visitante.json"), historico.RootElement.GetProperty("data").GetRawText());
            var inbox = await client.GetAsync("/api/atendimento/conversas");
            inbox.StatusCode.Should().Be(HttpStatusCode.OK);
            await File.WriteAllTextAsync(Path.Combine(pasta, "inbox.json"), await inbox.Content.ReadAsStringAsync());
            using var retratoInbox = JsonDocument.Parse(await inbox.Content.ReadAsStringAsync());
            var linha = retratoInbox.RootElement.GetProperty("data").EnumerateArray().Single(c => c.GetProperty("canal").GetString() == "ChatSite");
            linha.GetProperty("aguardaResposta").GetBoolean().Should().BeTrue();
            linha.GetProperty("ultimaMensagemTexto").GetString().Should().StartWith("Dúvida");
            var detalhes = await client.GetAsync($"/api/atendimento/conversas/{linha.GetProperty("id").GetGuid()}/mensagens");
            detalhes.StatusCode.Should().Be(HttpStatusCode.OK);
            using var listaConsole = JsonDocument.Parse(await detalhes.Content.ReadAsStringAsync());
            await File.WriteAllTextAsync(Path.Combine(pasta, "mensagens-console.json"), listaConsole.RootElement.GetProperty("data").GetRawText());
        }
        mensagens.Should().HaveCount(4);
        mensagens.Where(m => !m.GetProperty("doVisitante").GetBoolean()).Should().ContainSingle()
            .Which.GetProperty("texto").GetString().Should().Be(ConfirmacaoChat);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Canal == CanalConversa.ChatSite);
            conversa.AssumidaPorUsuarioId.Should().BeNull();
            conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
            conversa.MotivoEscalada.Should().Be("Visitante do site aguardando atendimento da equipe.");
            conversa.NaoLidas.Should().Be(3, "a confirmação automática de recebimento não responde à dúvida");
            conversa.AguardaResposta.Should().BeTrue();
            var ack = await db.AtendimentoMensagens.SingleAsync(m => m.ConversaId == conversa.Id && m.Direcao == DirecaoMensagem.Saida);
            ack.Autor.Should().Be(AutorMensagem.Sistema);
            ack.ExternoId.Should().StartWith("chatsite:");
            db.AtendimentoMensagens.Add(Mensagem.Saida(EmpresaId, conversa.Id, AutorMensagem.Sistema,
                DateTime.UtcNow, TipoConteudoMensagem.Texto, "Nota privada"));
            await db.SaveChangesAsync();
        }
        var final = await client.GetAsync($"/api/public/chat/{slug}/mensagens");
        (await final.Content.ReadAsStringAsync()).Should().NotContain("Nota privada");
        using (var limiteStream = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        using (var stream = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/public/chat/{slug}/stream"),
            HttpCompletionOption.ResponseHeadersRead, limiteStream.Token))
        {
            stream.StatusCode.Should().Be(HttpStatusCode.OK);
            using var leitor = new StreamReader(await stream.Content.ReadAsStreamAsync(limiteStream.Token));
            var confirmou = false;
            for (var i = 0; i < 30 && !confirmou; i++)
            {
                var linha = await leitor.ReadLineAsync(limiteStream.Token);
                linha.Should().NotContain("Nota privada");
                if (linha?.StartsWith("data: ", StringComparison.Ordinal) == true)
                {
                    using var evento = JsonDocument.Parse(linha[6..]);
                    confirmou = evento.RootElement.GetProperty("texto").GetString() == ConfirmacaoChat;
                }
            }
            confirmou.Should().BeTrue("a confirmação real chega também pelo stream da sessão");
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Canal == CanalConversa.ChatSite);
            // O domínio aceita identificador opaco: só a autoria Sistema identifica a confirmação.
            db.AtendimentoMensagens.Add(Mensagem.Saida(EmpresaId, conversa.Id, AutorMensagem.Dona,
                DateTime.UtcNow, TipoConteudoMensagem.Texto, "Resposta da equipe preservada", "chatsite:recepcao:resposta-humana"));
            await db.SaveChangesAsync();
        }
        var inboxHumana = await client.GetAsync("/api/atendimento/conversas");
        inboxHumana.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var retrato = JsonDocument.Parse(await inboxHumana.Content.ReadAsStringAsync()))
        {
            var linha = retrato.RootElement.GetProperty("data").EnumerateArray().Single(c => c.GetProperty("canal").GetString() == "ChatSite");
            linha.GetProperty("ultimaMensagemTexto").GetString().Should().Be("Resposta da equipe preservada");
            linha.GetProperty("aguardaResposta").GetBoolean().Should().BeFalse();
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var outroTenant = Guid.NewGuid();
            db.SetMobileTenantContext(outroTenant);
            db.Empresas.Add(new Empresa {Id = outroTenant, Nome = "Outra empresa", Documento = outroTenant.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow});
            var outra = EasyStock.Domain.Entities.Storefront.Storefront.Criar(outroTenant, "chat-outro-1742", "Outra loja", 0m);
            outra.Ativar(); db.Storefronts.Add(outra);
            db.TenantFeatureFlags.AddRange(
                TenantFeatureFlag.Criar(outroTenant, FeatureCatalogo.ModuloAtendimento, true, "teste@local.invalid"),
                TenantFeatureFlag.Criar(outroTenant, FeatureCatalogo.CanalChatSite, true, "teste@local.invalid"));
            await db.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/api/public/chat/chat-outro-1742/mensagens", new { texto = "Tentativa outra empresa" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Remove("X-Chat-Token");
        client.DefaultRequestHeaders.Add("X-Chat-Token", "token-inventado");
        (await client.PostAsJsonAsync($"/api/public/chat/{slug}/mensagens", new { texto = "Negativa" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
