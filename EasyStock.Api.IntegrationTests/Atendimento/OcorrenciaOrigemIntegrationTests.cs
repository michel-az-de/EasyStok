using System.Net;
using System.Net.Http.Json;
using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Atendimento.Ocorrencias;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task OcorrenciaNaOrigem_ResolveConversaDoPedido_ReplayNaoDuplicaERecusaPedidoAlheio()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()), NivelAcesso.Admin);
        using var http = factory.CreateClient();
        var cenario = await SemearAsync(factory);
        Guid pedidoId;
        Guid pedidoAlheioId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            using var bypass = db.UseRowLevelSecurityBypass();
            var pedido = Pedido.Criar(EmpresaId, origem: "whatsapp");
            pedido.ClienteId = cenario.ClienteId;
            db.Pedidos.Add(pedido);
            var conversa = await db.AtendimentoConversas.SingleAsync(c => c.Id == cenario.ConversaId);
            conversa.DefinirPedidoEmAndamento(pedido.Id);
            conversa.Assumir(DateTime.UtcNow, UsuarioId);
            pedidoId = pedido.Id;
            var outraEmpresa = Guid.NewGuid();
            var agora = DateTime.UtcNow;
            db.Empresas.Add(new Empresa { Id = outraEmpresa, Nome = "Outra empresa E02",
                Documento = outraEmpresa.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
            var alheio = Cliente.Criar(outraEmpresa, "Pessoa isolada E02");
            db.Clientes.Add(alheio);
            var pedidoAlheio = Pedido.Criar(outraEmpresa, origem: "whatsapp");
            pedidoAlheio.ClienteId = alheio.Id;
            db.Pedidos.Add(pedidoAlheio);
            pedidoAlheioId = pedidoAlheio.Id;
            await db.SaveChangesAsync();
        }
        var chave = Guid.NewGuid().ToString();
        const string relato = "Atraso registrado pela dona na origem Cozinha; conferir prazo com o cliente.";
        using var primeira = await Enviar(pedidoId, chave);
        primeira.StatusCode.Should().Be(HttpStatusCode.Created, await primeira.Content.ReadAsStringAsync());
        var criada = (await primeira.Content.ReadFromJsonAsync<ApiResponse<OcorrenciaDto>>())!.Data;
        criada.ConversaId.Should().Be(cenario.ConversaId);
        criada.Origem.Should().Be("dona");
        criada.Categoria.Should().Be("atraso");
        // A primeira resposta já foi produzida. Repetir a intenção após um ACK perdido reutiliza a mesma ocorrência.
        using var repetida = await Enviar(pedidoId, chave);
        repetida.StatusCode.Should().Be(HttpStatusCode.Created);
        repetida.Headers.GetValues("X-Idempotent-Replay").Should().Contain("true");
        (await repetida.Content.ReadFromJsonAsync<ApiResponse<OcorrenciaDto>>())!.Data.Id.Should().Be(criada.Id);
        using var alheia = await Enviar(pedidoAlheioId, Guid.NewGuid().ToString());
        alheia.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await alheia.Content.ReadFromJsonAsync<ApiErrorResponse>())!.Error.Message.Should().Be("Pedido não encontrado.");
        using var verificar = factory.Services.CreateScope();
        var atual = verificar.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        atual.SetMobileTenantContext(EmpresaId);
        (await atual.Set<Ocorrencia>().CountAsync(o => o.PedidoId == pedidoId)).Should().Be(1);
        var conversaAtual = await atual.AtendimentoConversas.AsNoTracking().SingleAsync(c => c.Id == cenario.ConversaId);
        conversaAtual.MotivoEscalada.Should().Contain(relato);
        conversaAtual.AssumidaPorUsuarioId.Should().BeNull("o atraso volta à fila humana mesmo quando a dona já havia assumido");
        (await atual.AtendimentoMensagens.CountAsync(m => m.ConversaId == cenario.ConversaId && m.Texto != null && m.Texto.Contains(relato))).Should().Be(1);

        async Task<HttpResponseMessage> Enviar(Guid pedido, string tentativa)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ocorrencias")
            {
                Content = JsonContent.Create(new { PedidoId = pedido, Categoria = "atraso", Relato = relato, ConversaId = (Guid?)null })
            };
            request.Headers.Add("Idempotency-Key", tentativa);
            return await http.SendAsync(request);
        }
    }
}
