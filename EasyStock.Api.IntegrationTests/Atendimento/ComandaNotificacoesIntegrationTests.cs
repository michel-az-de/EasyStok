using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    private static async Task PrepararAvisos(WebApplicationFactory<Program> factory, Guid conversaId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        await NotificacoesGlobaisSeed.ExecutarAsync(db, NullLogger.Instance);
        db.NotifConfiguracoesCanal.Add(ConfiguracaoCanal.Criar(CanalNotificacao.WhatsApp, "meta", EmpresaId));
        (await db.AtendimentoConversas.SingleAsync(c => c.Id == conversaId)).RegistrarEntrada(DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    private static async Task DespacharAvisos(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>().ExecutarRodadaAsync(100, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>().ExecutarRodadaAsync(TimeSpan.FromMinutes(1));
        await scope.ServiceProvider.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(1, 100);
    }

    private static async Task TurnoControlado(WebApplicationFactory<Program> factory, Guid conversaId, string texto)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Id == conversaId);
        conversa.LiberarAutomatico(); conversa.RegistrarEntrada(DateTime.UtcNow);
        db.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, conversa.Id, DateTime.UtcNow, TipoConteudoMensagem.Texto,
            texto, $"wamid.controlado.in.{Guid.NewGuid():N}"));
        await db.SaveChangesAsync();
        var agente = scope.ServiceProvider.GetRequiredService<AgenteAtendimentoService>();
        (await agente.ProcessarTurnoAsync(EmpresaId, conversaId, DateTime.UtcNow)).Respondeu.Should().BeTrue();
        (await agente.ProcessarTurnoAsync(EmpresaId, conversaId, DateTime.UtcNow)).ChamouLlm.Should().BeFalse("replay do turno não responde novamente");
    }

    [SkippableTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Comanda_PagamentoInicialRevalidaClienteNoDespachoAposEncerrarConversa(bool revogado, bool templateHumano)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        await using var factory = CriarFactory(mp, NivelAcesso.Admin).WithWebHostBuilder(b =>
        {
            b.UseSetting("Notifications:WhatsApp:Provider", "meta");
            b.UseSetting("Notifications:WhatsApp:Meta:AccessToken", "fixture-controlada-sem-acesso-externo");
            b.UseSetting("Notifications:WhatsApp:Meta:AppSecret", "fixture-controlada-sem-acesso-externo");
            b.UseSetting("Notifications:WhatsApp:Meta:VerifyToken", "fixture-controlada-sem-acesso-externo");
            b.ConfigureTestServices(s => s.AddSingleton(cloud));
        });
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var legado = await db.NotifTemplates.IgnoreQueryFilters().SingleAsync(t => t.Codigo == "pedido_pago_whatsapp_v1" && t.EmpresaId == null);
            legado.DefinirVersao(1);
            legado.DefinirMetadados("""{"template":"pedido_pago","idioma":"pt_BR"}""");
            if (templateHumano)
            {
                legado.AtualizarConteudo("", "Pagamento recebido", "operadora");
                legado.Aprovar("operadora"); legado.Ativar();
            }
            await db.SaveChangesAsync();
        }
        await PrepararAvisos(factory, c.ConversaId);
        var token = await Link(factory, c.ConversaId); var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, 1);
        // #1961 (#1717-12): a confirmação da comanda sai pelo WhatsApp no próprio envio. Este teste guarda o aviso do
        // pagamento, enfileirado depois; só ele não pode sair após o bloqueio ou a revogação.
        cloud.ReceivedCalls().Should().ContainSingle(x => x.GetArguments().OfType<string>().Any(a => a.Contains("Recebemos seu pedido")));
        cloud.ClearReceivedCalls();
        await ConfirmarPagamento(factory, envio.PedidoId, "690001", 15);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>().ExecutarRodadaAsync(100, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>().ExecutarRodadaAsync(TimeSpan.FromMinutes(1));
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var versoes = await db.NotifTemplates.IgnoreQueryFilters().Where(t => t.Codigo == "pedido_pago_whatsapp_v1").ToListAsync();
            versoes.Single(t => t.Versao == 1).Ativo.Should().Be(templateHumano);
            if (templateHumano) versoes.Should().ContainSingle(t => t.AtualizadoPor == "operadora");
            else versoes.Single(t => t.Versao == 2).Ativo.Should().BeTrue();
            // O pagamento também gera um Push interno para a equipe.
            var aviso = await db.NotifOutboxMensagens.SingleAsync(m => m.EmpresaId == EmpresaId
                && m.Evento!.Tipo == TipoEventoNotificacao.PedidoPagoConfirmado && m.Evento.RefEntidadeId == envio.PedidoId);
            aviso.Canal.Should().Be(CanalNotificacao.WhatsApp);
            using var metadata = JsonDocument.Parse(aviso.MetadadosJson!);
            metadata.RootElement.GetProperty("clienteId").GetString().Should().Be(c.ClienteId.ToString());
            metadata.RootElement.GetProperty("template").GetString().Should().Be("pedido_pago");
            (await db.AtendimentoConversas.SingleAsync(v => v.Id == c.ConversaId)).Encerrar(DateTime.UtcNow);
            if (revogado) db.ConsentimentosContato.Add(ConsentimentoContato.Registrar(EmpresaId, c.ClienteId,
                CanalConversa.WhatsApp, FinalidadeContato.Transacional, SituacaoConsentimento.Revogado, "fixture", DateTime.UtcNow));
            else (await db.Clientes.SingleAsync(v => v.Id == c.ClienteId)).Bloquear("fixture", DateTime.UtcNow);
            await db.SaveChangesAsync();
        }
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(1, 100);
            var aviso = await scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().NotifOutboxMensagens.AsNoTracking()
                .SingleAsync(m => m.EmpresaId == EmpresaId && m.Evento!.Tipo == TipoEventoNotificacao.PedidoPagoConfirmado
                    && m.Evento.RefEntidadeId == envio.PedidoId);
            aviso.Status.Should().Be(StatusOutbox.Falhado);
            aviso.ErroUltimaTentativa.Should().Contain(revogado ? "contato_transacional_revogado" : "cliente_bloqueado_ou_indisponivel");
            aviso.EnviadoEm.Should().BeNull();
        }
        cloud.ReceivedCalls().Should().BeEmpty("aviso já enfileirado não autoriza envio depois do bloqueio ou revogação");
    }

    [SkippableFact]
    public async Task Comanda_ConciliacaoExternaAtualizaReciboStreamENotificacaoDuravelSemNovoClique()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        var enviados = new ConcurrentQueue<string>();
        var falhar = false;
        var llm = new LlmBrowserControlado();
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        cloud.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            if (falhar) throw new WhatsAppCloudException(130429, "limite do canal controlado", ehPermanente: false, statusHttp: 429);
            enviados.Enqueue(c.ArgAt<string>(1));
            return new EnvioWhatsAppResult($"wamid.controlado.{Guid.NewGuid():N}");
        });
        cloud.EnviarTemplateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyList<(string Id, string Titulo)>?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            enviados.Enqueue("MODELO:" + c.ArgAt<string>(1));
            return new EnvioWhatsAppResult($"wamid.controlado.modelo.{Guid.NewGuid():N}");
        });
        await using var factory = CriarFactory(mp, NivelAcesso.Admin).WithWebHostBuilder(b =>
        {
            b.UseSetting("Notifications:WhatsApp:Provider", "meta");
            b.UseSetting("Notifications:WhatsApp:Meta:AccessToken", "fixture-controlada-sem-acesso-externo");
            b.UseSetting("Notifications:WhatsApp:Meta:AppSecret", "fixture-controlada-sem-acesso-externo");
            b.UseSetting("Notifications:WhatsApp:Meta:VerifyToken", "fixture-controlada-sem-acesso-externo");
            b.ConfigureTestServices(s => { s.AddSingleton(cloud); s.AddSingleton<IAgenteLlmClient>(llm); });
        });
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory); await PrepararAvisos(factory, c.ConversaId);
        var token = await Link(factory, c.ConversaId); var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 3)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, 1);
        var pagamento = new PagamentoMercadoPago("600001", "approved", null, envio.PedidoId.ToString(), 35, DateTime.UtcNow, "pix", "bank_transfer");
        mp.ConsultarPagamentoAsync("600001", Arg.Any<CancellationToken>()).Returns(pagamento);
        mp.ListarEstornosAsync("600001", Arg.Any<CancellationToken>()).Returns(Array.Empty<EstornoMercadoPagoResult>());
        mp.EstornarAsync("600001", 20, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new EstornoMercadoPagoResult("960001", 20, "pending", "600001", DateTime.UtcNow));
        await ConfirmarPagamento(factory, envio.PedidoId, "600001", 35);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            (await db.NotifEventos.CountAsync(e => e.Tipo == TipoEventoNotificacao.ComandaAtualizada)).Should().Be(0,
                "primeiro pagamento permanece no handler do template existente");
            var conversa = await db.AtendimentoConversas.SingleAsync(v => v.Id == c.ConversaId);
            db.Entry(conversa).Property(v => v.UltimaMensagemEntradaEm).CurrentValue = DateTime.UtcNow.AddHours(-25);
            await db.SaveChangesAsync();
        }
        await DespacharAvisos(factory);
        enviados.Count(m => m == "MODELO:pedido_pago").Should().Be(1, "primeiro pagamento mantém template aprovado configurado fora da janela");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            (await db.AtendimentoConversas.SingleAsync(v => v.Id == c.ConversaId)).RegistrarEntrada(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }
        var ajuste = await Link(factory, c.ConversaId); estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], Acerto = "estorno" });
        await Enviar(client, ajuste, 1);
        var resposta = await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar", new { Aceitar = true });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        estado = await Estado(client, ajuste);
        estado.Acerto!.Valor.Should().Be(20); estado.Acerto.Situacao.Should().Be("pendente");
        estado.Financeiro!.Devolvido.Should().Be(0); estado.Financeiro.Reservado.Should().Be(20);
        await DespacharAvisos(factory);
        enviados.Should().Contain(m => m.Contains("R$ 20,00") && m.Contains("Aguarde a confirmação") && m.Contains($"comanda nº {estado.NumeroComanda}"));

        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var stream = await client.GetAsync($"{Rota(ajuste)}/eventos", HttpCompletionOption.ResponseHeadersRead, prazo.Token);
        using var leitor = new StreamReader(await stream.Content.ReadAsStreamAsync(prazo.Token));
        await leitor.ReadLineAsync(prazo.Token); await leitor.ReadLineAsync(prazo.Token);
        mp.ConsultarEstornoAsync("600001", "960001", Arg.Any<CancellationToken>()).Returns(new EstornoMercadoPagoResult("960001", 20, "approved", "600001", DateTime.UtcNow));
        for (var i = 0; i < 2; i++)
        {
            using var scope = factory.Services.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<IEstornosOnlineService>().SincronizarPagamentoAsync(pagamento)).Should().BeTrue();
        }
        string? linha;
        do { linha = await leitor.ReadLineAsync(prazo.Token); } while (linha is not null && !linha.StartsWith("data:", StringComparison.Ordinal));
        var recebido = JsonSerializer.Deserialize<ComandaClienteResult>(linha![5..], ComandaClienteUseCase.Json)!;
        recebido.Envio!.Situacao.Should().Be("estorno_confirmado");
        recebido.Acerto!.Devolucoes.Should().ContainSingle().Which.Meio.Should().Be("pix");
        recebido.Financeiro!.Devolvido.Should().Be(20); recebido.Financeiro.Alocado.Should().Be(15);
        falhar = true; await DespacharAvisos(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var aviso = await db.NotifOutboxMensagens.SingleAsync(o => o.CorpoRenderizado.Contains("confirmado pelo Mercado Pago"));
            aviso.Status.Should().Be(StatusOutbox.Pendente); aviso.EnviadoEm.Should().BeNull();
            aviso.ProximaTentativaEm = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        }
        falhar = false; await DespacharAvisos(factory); await DespacharAvisos(factory);
        enviados.Count(m => m.Contains("confirmado pelo Mercado Pago")).Should().Be(1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            (await db.NotifOutboxMensagens.SingleAsync(o => o.CorpoRenderizado.Contains("confirmado pelo Mercado Pago"))).Status.Should().Be(StatusOutbox.Enviado);
            (await db.Set<PedidoEstornoOnline>().SingleAsync()).Situacao.Should().Be(PedidoEstornoOnline.Confirmado);
        }
        await TurnoControlado(factory, c.ConversaId, "Estornou? Tenho cupom?");
        using (var consulta = JsonDocument.Parse(llm.Resultados.Last()))
        {
            consulta.RootElement.GetProperty("financeiro").GetProperty("devolvido").GetDecimal().Should().Be(20);
            consulta.RootElement.GetProperty("acerto").GetProperty("situacao").GetString().Should().Be("confirmado");
            consulta.RootElement.GetProperty("faltaPagar").GetString().Should().Be("R$ 0,00");
        }
        await TurnoControlado(factory, c.ConversaId, "Quero adicionar mais itens");
        using (var compartilhamento = JsonDocument.Parse(llm.Resultados.Last()))
            compartilhamento.RootElement.GetProperty("enviado").GetBoolean().Should().BeTrue();
        (await client.PostAsJsonAsync($"{Rota(ajuste)}/nova-edicao", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Comanda_AcertoMistoNaoContaOperacaoRecusadaComoDinheiroDevolvido()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        await using var factory = CriarFactory(mp, NivelAcesso.Admin); using var client = factory.CreateClient();
        var c = await SemearAsync(factory); var token = await Link(factory, c.ConversaId); var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 3)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, 1);
        mp.ConsultarPagamentoAsync("610001", Arg.Any<CancellationToken>()).Returns(new PagamentoMercadoPago("610001", "approved", null, envio.PedidoId.ToString(), 35, DateTime.UtcNow, "pix", "bank_transfer"));
        await ConfirmarPagamento(factory, envio.PedidoId, "610001", 35);
        var acrescimo = await Link(factory, c.ConversaId); estado = await Estado(client, acrescimo);
        await Salvar(client, acrescimo, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 4)] }); await Enviar(client, acrescimo, 1);
        (await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar", new { Aceitar = true })).EnsureSuccessStatusCode();
        mp.ConsultarPagamentoAsync("610002", Arg.Any<CancellationToken>()).Returns(new PagamentoMercadoPago("610002", "approved", null, envio.PedidoId.ToString(), 10, DateTime.UtcNow, "pix", "bank_transfer"));
        await ConfirmarPagamento(factory, envio.PedidoId, "610002", 10);
        mp.ListarEstornosAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<EstornoMercadoPagoResult>());
        mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            new EstornoMercadoPagoResult(call.ArgAt<string>(0) == "610001" ? "961001" : "961002", call.Arg<decimal>(),
                call.ArgAt<string>(0) == "610002" ? "approved" : "rejected", call.ArgAt<string>(0), DateTime.UtcNow));
        var ajuste = await Link(factory, c.ConversaId); estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], Acerto = "estorno" }); await Enviar(client, ajuste, 1);
        (await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar", new { Aceitar = true })).EnsureSuccessStatusCode();
        estado = await Estado(client, ajuste);
        estado.Envio!.Situacao.Should().Be("estorno_recusado"); estado.Acerto!.Valor.Should().Be(30);
        estado.Acerto.Devolucoes.Should().HaveCount(2);
        estado.Acerto.Devolucoes.Where(d => d.Situacao == "confirmado").Sum(d => d.Valor).Should().Be(10);
        estado.Acerto.Devolucoes.Where(d => d.Situacao == "recusado").Sum(d => d.Valor).Should().Be(20);
        estado.Financeiro!.Devolvido.Should().Be(10); estado.Financeiro.Reservado.Should().Be(0); estado.Financeiro.Alocado.Should().Be(35);
    }
}
