using System.Collections.Concurrent;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
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
        var estornos = new ConcurrentDictionary<string, string>();
        var estornoId = 900000;
        mp.ListarEstornosAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<EasyStock.Application.Ports.Output.Pagamentos.EstornoMercadoPagoResult>());
        mp.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c =>
            new EasyStock.Application.Ports.Output.Pagamentos.EstornoMercadoPagoResult(
                estornos.GetOrAdd(c.ArgAt<string>(2), _ => Interlocked.Increment(ref estornoId).ToString()),
                c.Arg<decimal>(), "approved", c.ArgAt<string>(0), DateTime.UtcNow));
        var mensagens = new ConcurrentQueue<string>();
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        cloud.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            mensagens.Enqueue(c.ArgAt<string>(1));
            return new EnvioWhatsAppResult($"wamid.fixture.{Guid.NewGuid():N}");
        });
        await using var factory = CriarFactory(mp, NivelAcesso.Admin).WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton(cloud);
                s.AddSingleton<IStartupFilter>(new ComandaBrowserBridge(mp, mensagens));
            }));
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        var cenario = await SemearAsync(factory, "casa-da-baba");
        string token;
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
        }
        var estado = await Estado(client, token);
        var dados = new { api = client.BaseAddress!.ToString().TrimEnd('/'), empresaId = EmpresaId, usuarioId = UsuarioId,
            conversaId = cenario.ConversaId, clienteId = cenario.ClienteId, token, numeroComanda = estado.NumeroComanda,
            cardapioItemId = cenario.CardapioItemId, janelaId = cenario.JanelaId, dataEntrega = cenario.DataEntrega };
        await File.WriteAllTextAsync(arquivo!, JsonSerializer.Serialize(dados, ComandaClienteUseCase.Json));
        using var limite = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        while (!File.Exists(arquivo + ".stop")) await Task.Delay(500, limite.Token);
    }

    private sealed record PagamentoBrowser(Guid PedidoId, decimal Valor);
    private sealed record AgenteBrowser(Guid ConversaId);

    private sealed class ComandaBrowserBridge(EasyStock.Application.Ports.Output.Pagamentos.IMercadoPagoClient mp,
        ConcurrentQueue<string> mensagens) : IStartupFilter
    {
        private int _pagamentoId = 800000;
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuar) =>
            {
                if (!context.Request.Path.StartsWithSegments("/__comanda-teste")) { await continuar(); return; }
                // Este middleware existe exclusivamente na fixture, escuta localhost e nunca entra na API publicada.
                context.RequestServices.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(EmpresaId);
                if (context.Request.Path == "/__comanda-teste/mensagens")
                {
                    await context.Response.WriteAsJsonAsync(mensagens.ToArray()); return;
                }
                if (context.Request.Method == "POST" && context.Request.Path == "/__comanda-teste/pagamento")
                {
                    var dados = (await context.Request.ReadFromJsonAsync<PagamentoBrowser>())!;
                    var id = Interlocked.Increment(ref _pagamentoId).ToString();
                    mp.ConsultarPagamentoAsync(id, Arg.Any<CancellationToken>()).Returns(new EasyStock.Application.Ports.Output.Pagamentos.PagamentoMercadoPago(
                        id, "approved", null, dados.PedidoId.ToString(), dados.Valor, DateTime.UtcNow, "pix", "bank_transfer"));
                    var resultado = await context.RequestServices.GetRequiredService<ConfirmarPagamentoPedidoUseCase>()
                        .ExecuteAsync(new(dados.PedidoId, id, "approved", dados.Valor, "pix"));
                    await context.Response.WriteAsJsonAsync(resultado); return;
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
