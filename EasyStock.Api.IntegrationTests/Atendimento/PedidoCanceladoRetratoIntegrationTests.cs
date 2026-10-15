using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PedidoCancelado_RetratoDaApiPreservaCobrancaERecebimentos(bool sinalPago)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new ConcurrentDictionary<string, CriarPreferenceCommand>());
        mp.ObterContaRecebedoraAsync(Arg.Any<CancellationToken>()).Returns("conta-da-prova");
        mp.BuscarPagamentosPorReferenciaAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<PagamentoMercadoPago>());
        await using var factory = CriarFactory(mp, NivelAcesso.Admin);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var criado = await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/pedido", new {
            Itens = new[] { new { CardapioItemId = c.CardapioItemId, Qtd = 6 } },
            c.JanelaId, c.DataEntrega, Forma = "online" });
        criado.StatusCode.Should().Be(HttpStatusCode.OK, await criado.Content.ReadAsStringAsync());
        using var criacao = JsonDocument.Parse(await criado.Content.ReadAsStringAsync());
        var envio = criacao.RootElement.GetProperty("data").Deserialize<PedidoConversaGeradoResult>(ComandaClienteUseCase.Json)!;
        envio.Cobranca!.Valor.Should().Be(32.50m);
        if (sinalPago)
        {
            await ConfirmarPagamento(factory, envio.PedidoId, "173901", 32.50m);
            var cancelar = await client.PostAsJsonAsync($"/api/pedidos/{envio.PedidoId}/cancelar", new { EmpresaId, Id = envio.PedidoId, Motivo = "Teste de cancelamento com sinal preservado" });
            cancelar.StatusCode.Should().Be(HttpStatusCode.OK, await cancelar.Content.ReadAsStringAsync());
        }
        else
        {
            for (var tentativa = 1; tentativa <= 2; tentativa++)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                db.SetMobileTenantContext(EmpresaId);
                var cobranca = await db.Set<CobrancaPedido>().SingleAsync(p => p.PedidoId == envio.PedidoId && p.Status == StatusCobrancaPedido.Pendente);
                db.Entry(cobranca).Property(p => p.ExpiraEm).CurrentValue = DateTime.UtcNow.AddMinutes(-1);
                await db.SaveChangesAsync();
                var resultado = await scope.ServiceProvider.GetRequiredService<ProcessarCobrancaVencidaUseCase>()
                    .ExecuteAsync(new CobrancaPedidoVencida(cobranca.Id, EmpresaId, envio.PedidoId, cobranca.ContaRecebedoraId));
                resultado.Should().Be(tentativa == 1 ? ResultadoExpiracaoCobranca.Reemitida : ResultadoExpiracaoCobranca.PedidoCancelado);
            }
        }
        var resposta = await client.GetAsync($"/api/atendimento/conversas/{c.ConversaId}/pedido");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using var retrato = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var json = retrato.RootElement.GetProperty("data").GetRawText();
        var pedido = JsonSerializer.Deserialize<PedidoConversaResult>(json, ComandaClienteUseCase.Json)!;
        pedido.Status.Should().Be("cancelado");
        pedido.Total.Should().Be(65m);
        pedido.TotalPago.Should().Be(sinalPago ? 32.50m : 0m);
        pedido.Cobranca!.Status.Should().Be(sinalPago ? "Paga" : "Expirada");
        pedido.Cobranca.Tentativa.Should().Be(sinalPago ? 1 : 2);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            (await db.Set<PedidoPagamento>().CountAsync(p => p.PedidoId == envio.PedidoId)).Should().Be(sinalPago ? 1 : 0);
            (await db.Set<PedidoEstornoOnline>().AnyAsync(p => p.PedidoId == envio.PedidoId)).Should().BeFalse();
            (await db.Set<CreditoClienteComanda>().AnyAsync()).Should().BeFalse();
        }
        if (Environment.GetEnvironmentVariable("EVIDENCIAS_PEDIDO_FINAL") is { Length: > 0 } pasta)
        {
            Directory.CreateDirectory(pasta);
            await File.WriteAllTextAsync(Path.Combine(pasta, sinalPago ? "cancelado-sinal.json" : "cancelado-segundo-link.json"), json);
        }
    }
}
