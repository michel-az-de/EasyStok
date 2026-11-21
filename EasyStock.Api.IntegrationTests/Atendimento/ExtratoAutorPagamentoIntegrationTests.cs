using System.Net.Http.Json;
using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task ExtratoDaConversa_PreservaMesmoAutorPersistidoDoDetalheDoPedidoEAutorLegadoNulo()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new()));
        using var http = factory.CreateClient();
        var cenario = await SemearAsync(factory);
        Guid pedidoId;
        Guid pagamentoId;
        Guid legadoId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var pedido = Pedido.Criar(EmpresaId, origem: "whatsapp");
            pedido.ClienteId = cenario.ClienteId;
            pedido.Status = StatusPedidoMapper.Aguardando;
            pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Pedido J05",
                Quantidade = 1, PrecoUnitario = 33m, Subtotal = 33m, CriadoEm = DateTime.UtcNow });
            pedido.RecalcularTotal();
            var pagamento = new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix",
                Valor = 10m, PagoEm = DateTime.UtcNow, RegistradoPorUserId = UsuarioId, RegistradoPorNome = "Dona R4" };
            var legado = new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix",
                Valor = 5m, PagoEm = DateTime.UtcNow.AddMinutes(-1) };
            pedido.Pagamentos.Add(pagamento);
            pedido.Pagamentos.Add(legado);
            db.Pedidos.Add(pedido);
            (await db.AtendimentoConversas.SingleAsync(c => c.Id == cenario.ConversaId)).DefinirPedidoEmAndamento(pedido.Id);
            await db.SaveChangesAsync();
            pedidoId = pedido.Id;
            pagamentoId = pagamento.Id;
            legadoId = legado.Id;
        }

        var detalhe = (await http.GetFromJsonAsync<ApiResponse<PedidoDetalheResult>>($"/api/pedidos/{pedidoId}"))!.Data;
        var conversa = (await http.GetFromJsonAsync<ApiResponse<PedidoConversaResult>>($"/api/atendimento/conversas/{cenario.ConversaId}/pedido"))!.Data;
        var origem = detalhe.Pagamentos.Single(p => p.Id == pagamentoId);
        var extrato = conversa.Pagamentos!.Single(p => p.Id == pagamentoId);
        origem.RegistradoPorNome.Should().Be("Dona R4");
        extrato.RegistradoPorNome.Should().Be(origem.RegistradoPorNome);
        extrato.Valor.Should().Be(origem.Valor);
        extrato.Metodo.Should().Be(origem.Metodo);
        extrato.PagoEm.Should().Be(origem.PagoEm);
        conversa.Pagamentos!.Single(p => p.Id == legadoId).RegistradoPorNome.Should().BeNull();
        conversa.TotalPago.Should().Be(15m);
    }
}
