using System.Collections.Concurrent;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task Comanda_FixtureLocalParaNavegador()
    {
        var arquivo = Environment.GetEnvironmentVariable("EASYSTOK_COMANDA_BROWSER_FIXTURE");
        Skip.If(string.IsNullOrWhiteSpace(arquivo), "Fixture interativa requer EASYSTOK_COMANDA_BROWSER_FIXTURE explícito.");
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        var estornos = new EstornosBrowserControlados(mp);
        var llm = new LlmBrowserControlado();
        var mensagens = new ConcurrentQueue<string>();
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        cloud.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            mensagens.Enqueue(c.ArgAt<string>(1));
            return new EnvioWhatsAppResult($"wamid.fixture.{Guid.NewGuid():N}");
        });
        await using var factory = CriarFactory(mp, NivelAcesso.Admin).WithWebHostBuilder(b =>
        {
            b.UseSetting("Notifications:WhatsApp:Provider", "meta");
            b.UseSetting("Notifications:WhatsApp:Meta:AccessToken", "fixture-controlada-sem-acesso-externo");
            b.UseSetting("Notifications:WhatsApp:Meta:AppSecret", "fixture-controlada-sem-acesso-externo");
            b.UseSetting("Notifications:WhatsApp:Meta:VerifyToken", "fixture-controlada-sem-acesso-externo");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton(cloud);
                s.AddSingleton<IAgenteLlmClient>(llm);
                s.AddSingleton<IStartupFilter>(new ComandaBrowserBridge(mp, mensagens, estornos, llm));
            });
        });
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        var cenario = await SemearAsync(factory, "casa-da-baba");
        await PrepararAvisos(factory, cenario.ConversaId);
        string token, leadToken;
        Guid leadConversaId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var agora = DateTime.UtcNow;
            db.Usuarios.Add(new Usuario { Id = UsuarioId, Nome = "Dona de teste", Email = "dona@fixture.test", SenhaHash = "fixture-sem-login-externo", Ativo = true, CriadoEm = agora, AlteradoEm = agora });
            db.UsuariosEmpresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = UsuarioId, EmpresaId = EmpresaId, Ativo = true, CriadoEm = agora });
            var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Id == cenario.ConversaId);
            conversa.RegistrarEntrada(agora);
            db.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, conversa.Id, agora, TipoConteudoMensagem.Texto,
                "Olá, quero montar minha comanda.", $"wamid.fixture.entrada.{Guid.NewGuid():N}"));
            await db.SaveChangesAsync();
            var link = await scope.ServiceProvider.GetRequiredService<LinkCardapioConversaService>()
                .GerarAsync(EmpresaId, cenario.ConversaId, DateTime.UtcNow);
            token = link.Token;
            var lead = Conversa.Abrir(EmpresaId, "5511912345678", agora, "Cliente da sprint");
            db.AtendimentoConversas.Add(lead);
            await db.SaveChangesAsync();
            leadConversaId = lead.Id;
            leadToken = (await scope.ServiceProvider.GetRequiredService<LinkCardapioConversaService>()
                .GerarAsync(EmpresaId, lead.Id, agora)).Token;
        }
        var estado = await Estado(client, token);
        var dados = new { api = client.BaseAddress!.ToString().TrimEnd('/'), empresaId = EmpresaId, usuarioId = UsuarioId,
            conversaId = cenario.ConversaId, clienteId = cenario.ClienteId, token, numeroComanda = estado.NumeroComanda,
            cardapioItemId = cenario.CardapioItemId, janelaId = cenario.JanelaId, dataEntrega = cenario.DataEntrega,
            leadToken, leadConversaId };
        await File.WriteAllTextAsync(arquivo!, JsonSerializer.Serialize(dados, ComandaClienteUseCase.Json));
        using var limite = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        while (!File.Exists(arquivo + ".stop")) await Task.Delay(500, limite.Token);
    }

    private sealed record PagamentoBrowser(Guid PedidoId, decimal Valor, string? PagamentoExternoId = null);
    private sealed record AgenteBrowser(Guid ConversaId);
    private sealed record TurnoBrowser(Guid ConversaId, string Texto, string? RespostaControlada = null, bool RepetirUltimoTurno = false,
        bool ConsultarAntesDaRespostaControlada = false);
    private sealed record EstornoBrowser(Guid PedidoId, string Situacao);

    private sealed class ComandaBrowserBridge(EasyStock.Application.Ports.Output.Pagamentos.IMercadoPagoClient mp,
        ConcurrentQueue<string> mensagens, EstornosBrowserControlados estornos, LlmBrowserControlado llm,
        ConcurrentQueue<MensagemCanalGoLive>? detalhes = null) : IStartupFilter
    {
        private int _pagamentoId = 800000;
        private readonly ConcurrentDictionary<string, EasyStock.Application.Ports.Output.Pagamentos.PagamentoMercadoPago> _pagamentos = new();
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuar) =>
            {
                if (!context.Request.Path.StartsWithSegments("/__comanda-teste")) { await continuar(); return; }
                // Este middleware existe exclusivamente na fixture, escuta localhost e nunca entra na API publicada.
                if (context.Connection.RemoteIpAddress is not { } ip || !System.Net.IPAddress.IsLoopback(ip))
                { context.Response.StatusCode = 403; return; }
                context.RequestServices.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
                if (context.Request.Method == "GET" && context.Request.Path == "/__comanda-teste/evidencias")
                {
                    ComandaClienteResult? estado = null;
                    if (!string.IsNullOrWhiteSpace(context.Request.Query["token"]))
                    {
                        var comanda = context.RequestServices.GetRequiredService<ComandaClienteUseCase>();
                        var acesso = await comanda.AcessoAsync(context.Request.Query["token"].ToString(), CancellationToken.None);
                        if (acesso.EmpresaId != EmpresaId) { context.Response.StatusCode = 404; return; }
                        estado = await comanda.EstadoAsync(acesso, CancellationToken.None);
                    }
                    if (!Guid.TryParse(context.Request.Query["pedidoId"], out var pedidoId))
                    {
                        if (estado is null) { context.Response.StatusCode = 400; return; }
                        await context.Response.WriteAsJsonAsync(new { estado }); return;
                    }
                    if (estado is not null && estado.Pedido?.PedidoId != pedidoId) { context.Response.StatusCode = 404; return; }
                    var banco = context.RequestServices.GetRequiredService<EasyStockDbContext>();
                    var pedido = await banco.Pedidos.AsNoTracking().Include(p => p.Pagamentos)
                        .SingleOrDefaultAsync(p => p.EmpresaId == EmpresaId && p.Id == pedidoId);
                    if (pedido is null) { context.Response.StatusCode = 404; return; }
                    var financeiro = await context.RequestServices.GetRequiredService<EasyStock.Application.Services.Pedidos.SaldoFinanceiroPedido>().ConsultarAsync(pedido, CancellationToken.None);
                    await context.Response.WriteAsJsonAsync(new { pedidoId, pedido.ClienteId, total = pedido.Total.Valor, financeiro, estado,
                        pagamentos = pedido.Pagamentos.Select(p => new { p.Id, p.Valor }),
                        cobrancas = await banco.CobrancasPedido.AsNoTracking().Where(c => c.EmpresaId == EmpresaId && c.PedidoId == pedidoId)
                            .Select(c => new { c.Id, c.Valor, c.Status, c.PagamentoExternoId }).ToListAsync(),
                        estornos = await banco.Set<PedidoEstornoOnline>().AsNoTracking().Where(e => e.EmpresaId == EmpresaId && e.PedidoId == pedidoId).ToListAsync(),
                        creditos = await banco.Set<CreditoClienteComanda>().AsNoTracking().Where(c => c.EmpresaId == EmpresaId && c.ClienteId == pedido.ClienteId)
                            .Select(c => new { c.Id, c.PedidoOrigemId, c.OperacaoId, c.Codigo, c.Valor, c.Saldo }).ToListAsync(),
                        usosCupom = await banco.Set<UsoCreditoClienteComanda>().AsNoTracking().Where(u => u.EmpresaId == EmpresaId && u.PedidoId == pedidoId).ToListAsync(),
                        movimentosCaixaDoTenant = await banco.MovimentosCaixa.AsNoTracking().Where(m => m.EmpresaId == EmpresaId)
                            .Select(m => new { m.Id, m.Tipo, m.Valor, m.Referencia, m.Categoria }).ToListAsync() }); return;
                }
                if (context.Request.Path == "/__comanda-teste/mensagens-detalhadas")
                { await context.Response.WriteAsJsonAsync(detalhes?.ToArray() ?? []); return; }
                if (context.Request.Path == "/__comanda-teste/mensagens")
                {
                    await context.Response.WriteAsJsonAsync(mensagens.ToArray()); return;
                }
                if (context.Request.Method == "POST" && context.Request.Path == "/__comanda-teste/notificacoes")
                {
                    await context.RequestServices.GetRequiredService<IIntegrationEventDispatcher>().ExecutarRodadaAsync(100, CancellationToken.None);
                    await context.RequestServices.GetRequiredService<INotificacoesAvaliadorOrchestrator>().ExecutarRodadaAsync(TimeSpan.FromMinutes(1));
                    await context.RequestServices.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(1, 100);
                    var db = context.RequestServices.GetRequiredService<EasyStockDbContext>();
                    await context.Response.WriteAsJsonAsync(await db.NotifOutboxMensagens.AsNoTracking().Select(o => new
                        { o.Id, o.IdempotencyKey, o.Destinatario, o.CorpoRenderizado, o.Status, o.EnviadoEm, o.ProviderMensagemId, o.ErroUltimaTentativa }).ToListAsync()); return;
                }
                if (context.Request.Method == "POST" && context.Request.Path == "/__comanda-teste/estorno-modo")
                {
                    var dados = (await context.Request.ReadFromJsonAsync<EstornoBrowser>())!;
                    if (dados.PedidoId == Guid.Empty) estornos.DefinirModo(dados.Situacao);
                    else
                    {
                        var banco = context.RequestServices.GetRequiredService<EasyStockDbContext>();
                        if (!await banco.Pedidos.AnyAsync(p => p.EmpresaId == EmpresaId && p.Id == dados.PedidoId))
                        { context.Response.StatusCode = 404; return; }
                        var ids = await banco.CobrancasPedido.Where(p => p.EmpresaId == EmpresaId && p.PedidoId == dados.PedidoId
                            && p.PagamentoExternoId != null).Select(p => p.PagamentoExternoId!).ToListAsync();
                        estornos.DefinirModo(dados.Situacao, ids);
                    }
                    await context.Response.WriteAsJsonAsync(new { configurado = true }); return;
                }
                if (context.Request.Method == "POST" && context.Request.Path == "/__comanda-teste/conciliar-estorno")
                {
                    var dados = (await context.Request.ReadFromJsonAsync<EstornoBrowser>())!;
                    var db = context.RequestServices.GetRequiredService<EasyStockDbContext>();
                    var pendentes = await db.Set<PedidoEstornoOnline>().AsNoTracking().Where(e => e.EmpresaId == EmpresaId
                        && e.PedidoId == dados.PedidoId).Select(e => e.PagamentoExternoId).Distinct().ToListAsync();
                    foreach (var pagamentoId in pendentes)
                    {
                        estornos.Conciliar(pagamentoId, dados.Situacao);
                        var pagamento = await mp.ConsultarPagamentoAsync(pagamentoId);
                        await context.RequestServices.GetRequiredService<IEstornosOnlineService>().SincronizarPagamentoAsync(pagamento!);
                    }
                    await context.Response.WriteAsJsonAsync(new { conciliado = true }); return;
                }
                if (context.Request.Method == "POST" && context.Request.Path == "/__comanda-teste/atendimento-automatico")
                {
                    var dados = (await context.Request.ReadFromJsonAsync<TurnoBrowser>())!;
                    var resultados = llm.IniciarTurno(dados.RespostaControlada, dados.ConsultarAntesDaRespostaControlada);
                    var db = context.RequestServices.GetRequiredService<EasyStockDbContext>();
                    var conversa = await db.AtendimentoConversas.SingleAsync(c => c.EmpresaId == EmpresaId && c.Id == dados.ConversaId);
                    if (!dados.RepetirUltimoTurno)
                    {
                        conversa.LiberarAutomatico(); conversa.RegistrarEntrada(DateTime.UtcNow);
                        db.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, conversa.Id, DateTime.UtcNow,
                            TipoConteudoMensagem.Texto, dados.Texto, $"wamid.fixture.in.{Guid.NewGuid():N}"));
                    }
                    await db.SaveChangesAsync();
                    var resultado = await context.RequestServices.GetRequiredService<AgenteAtendimentoService>()
                        .ProcessarTurnoAsync(EmpresaId, conversa.Id, DateTime.UtcNow);
                    await context.Response.WriteAsJsonAsync(new { resultado, ferramentas = resultados.ToArray() }); return;
                }
                if (context.Request.Method == "POST" && context.Request.Path == "/__comanda-teste/pagamento")
                {
                    var dados = (await context.Request.ReadFromJsonAsync<PagamentoBrowser>())!;
                    var db = context.RequestServices.GetRequiredService<EasyStockDbContext>();
                    if (!await db.Pedidos.AnyAsync(p => p.EmpresaId == EmpresaId && p.Id == dados.PedidoId))
                    { context.Response.StatusCode = 404; return; }
                    var id = dados.PagamentoExternoId ?? Interlocked.Increment(ref _pagamentoId).ToString();
                    if (!long.TryParse(id, out var numero) || numero <= 0) { context.Response.StatusCode = 400; return; }
                    var pagamento = _pagamentos.GetOrAdd(id, _ => new EasyStock.Application.Ports.Output.Pagamentos.PagamentoMercadoPago(
                        id, "approved", null, dados.PedidoId.ToString(), dados.Valor, DateTime.UtcNow, "pix", "bank_transfer"));
                    if (pagamento.ExternalReference != dados.PedidoId.ToString() || pagamento.TransactionAmount != dados.Valor)
                    { context.Response.StatusCode = 409; return; }
                    mp.ConsultarPagamentoAsync(id, Arg.Any<CancellationToken>()).Returns(pagamento);
                    var resultado = await context.RequestServices.GetRequiredService<ConfirmarPagamentoPedidoUseCase>()
                        .ExecuteAsync(new(dados.PedidoId, id, "approved", dados.Valor, "pix"));
                    await context.Response.WriteAsJsonAsync(new { resultado.Situacao, resultado.CobrancaId, resultado.StatusPedido,
                        resultado.Confirmado, pagamentoExternoId = id }); return;
                }
                if (context.Request.Method == "POST" && context.Request.Path == "/__comanda-teste/compartilhar-agente")
                {
                    var dados = (await context.Request.ReadFromJsonAsync<AgenteBrowser>())!;
                    var conversa = await context.RequestServices.GetRequiredService<IConversaRepository>().ObterPorIdAsync(EmpresaId, dados.ConversaId);
                    var resultado = await context.RequestServices.GetServices<IFerramentaAgente>().OfType<EnviarCardapioImagemFerramenta>().Single()
                        .ExecutarAsync(new(EmpresaId, conversa!, DateTime.UtcNow), JsonSerializer.SerializeToElement(new { }));
                    await context.RequestServices.GetRequiredService<IUnitOfWork>().CommitAsync();
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(resultado); return;
                }
                context.Response.StatusCode = 404;
            });
            next(app);
        };
    }
}
