using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableTheory]
    [InlineData(NivelAcesso.Operador)]
    [InlineData(NivelAcesso.Admin)]
    public async Task Comanda_ExcedenteMantemConferenciaHumanaSem403AposSalvar(NivelAcesso nivel)
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new ConcurrentDictionary<string, EasyStock.Application.Ports.Output.Pagamentos.CriarPreferenceCommand>()), nivel);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var original = await Enviar(client, token, 1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var pedido = await db.Pedidos.SingleAsync(p => p.Id == original.PedidoId);
            pedido.Status = StatusPedidoMapper.Aguardando;
            db.Set<PedidoPagamento>().Add(new() { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = 40m, PagoEm = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var ajuste = await Link(factory, c.ConversaId);
        estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)], Acerto = "cupom" });
        var resposta = await client.PostAsJsonAsync($"{Rota(ajuste)}/confirmar", new { revisao = 1 });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        var envio = (await resposta.Content.ReadFromJsonAsync<EnvioComandaCliente>())!;
        envio.Situacao.Should().Be("alteracao_aguardando_confirmacao");
        (await Enviar(client, ajuste, 1)).Should().BeEquivalentTo(envio);
        var relido = await Estado(client, ajuste);
        relido.Rascunho.Itens.Single().Qtd.Should().Be(2);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var pedido = await db.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == original.PedidoId);
            pedido.Total.Valor.Should().Be(15m); pedido.TotalPago.Should().Be(40m);
            (await db.Set<CreditoClienteComanda>().AnyAsync()).Should().BeFalse();
            (await db.Set<PedidoEstornoOnline>().AnyAsync()).Should().BeFalse();
            var uc = scope.ServiceProvider.GetRequiredService<ConfirmarAlteracaoComandaUseCase>();
            await uc.Invoking(u => u.ExecuteAsync(new(Guid.NewGuid(), c.ConversaId, relido.LinkId, true, UsuarioId, "Teste", NivelAcesso.Admin), CancellationToken.None))
                .Should().ThrowAsync<ComandaClienteConflitoException>();
        }
        if (nivel == NivelAcesso.Admin)
        {
            foreach (var (empresa, permitido, esperado) in new[] {
                (Guid.NewGuid(), true, HttpStatusCode.Conflict), (EmpresaId, false, HttpStatusCode.Forbidden) })
            {
                await using var negativa = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => {
                    s.RemoveAll<ICurrentUserAccessor>();
                    s.AddSingleton<ICurrentUserAccessor>(new UsuarioExcedenteStub(empresa, permitido));
                }));
                using var acesso = negativa.CreateClient();
                var negada = await acesso.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{relido.LinkId}/confirmar", new { Aceitar = true });
                negada.StatusCode.Should().Be(esperado, await negada.Content.ReadAsStringAsync());
            }
        }
        var humana = await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{relido.LinkId}/confirmar", new { Aceitar = true });
        humana.StatusCode.Should().Be(nivel == NivelAcesso.Admin ? HttpStatusCode.OK : HttpStatusCode.Forbidden, await humana.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var pedido = await db.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == original.PedidoId);
            pedido.Total.Valor.Should().Be(nivel == NivelAcesso.Admin ? 25m : 15m);
            pedido.TotalPago.Should().Be(40m);
            (await db.Set<CreditoClienteComanda>().CountAsync()).Should().Be(nivel == NivelAcesso.Admin ? 1 : 0);
            if (nivel == NivelAcesso.Admin)
            {
                var cupom = await db.Set<CreditoClienteComanda>().SingleAsync();
                var eventos = await db.NotifEventos.Where(e => e.RefEntidadeId == pedido.Id).ToListAsync();
                var mensagem = eventos.Select(e => JsonSerializer.Deserialize<Dictionary<string, string?>>(e.PayloadJson)!)
                    .Where(e => e.ContainsKey("mensagem")).Select(e => e["mensagem"])
                    .Single(m => m!.Contains(cupom.Codigo));
                mensagem.Should().Contain("Seu pedido foi atualizado. Total: R$ 25,00.")
                    .And.Contain($"Código do cupom: {cupom.Codigo}")
                    .And.Contain("Você tem R$ 15,00 de crédito para uma próxima compra.")
                    .And.Contain("Esse crédito não é uma devolução em dinheiro.");
            }
        }
    }

    private sealed class UsuarioExcedenteStub(Guid empresaId, bool permitido) : ICurrentUserAccessor
    {
        public Guid EmpresaId => empresaId;
        public bool IsAuthenticated => true;
        public Guid UsuarioId => PedidoConversaConcorrenciaTests.UsuarioId;
        public NivelAcesso Nivel => NivelAcesso.Admin;
        public bool TemPermissao(Permissao permissao) => permitido;
    }
}
