using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #2813: os pedidos entregues do histórico entram quitados, sem pagamento, sem movimento de caixa e sem conta a receber,
/// e o ERP os reconhece pela marca no pedido. Aguardando e cancelado do histórico não levam a marca e seguem como antes.
/// </summary>
[Collection("MobileE2E")]
public class LegacySnapshotImportPagosSemCaixaE2ETests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task PrimeiraCarga_EntreguesEntramQuitadosSemCaixa_AguardandoECanceladoSeguemSemMarca()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var device = await fixture.SeedMobileDeviceAsync(empresa, loja);
        var produto = "p-" + Guid.NewGuid().ToString("N");
        var cliente = "c-" + Guid.NewGuid().ToString("N");
        string[] entregues = ["o-" + Guid.NewGuid().ToString("N"), "o-" + Guid.NewGuid().ToString("N"), "o-" + Guid.NewGuid().ToString("N")];
        var aguardando = "o-" + Guid.NewGuid().ToString("N");
        var cancelado = "o-" + Guid.NewGuid().ToString("N");
        var quando = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        object Ordem(string id, string status, decimal preco) => new
        {
            id, clientId = cliente, clientSnapshot = new { name = "Cliente " + cliente },
            items = new[] { new { productId = produto, name = "Produto " + produto, qty = 1, unitPrice = preco } },
            total = preco, status, createdAt = quando, updatedAt = quando
        };
        var estado = new
        {
            products = new[] { new { id = produto, name = "Produto " + produto, category = "massa", stock = 7, price = 10m, unit = "un" } },
            clients = new[] { new { id = cliente, name = "Cliente " + cliente, apt = "62", address = "Endereco", phone = "11999999999", lastOrder = quando, orderCount = 5 } },
            orders = new[] { Ordem(entregues[0], "entregue", 30m), Ordem(entregues[1], "entregue", 45m), Ordem(entregues[2], "entregue", 12.5m),
                Ordem(aguardando, "aguardando", 22m), Ordem(cancelado, "cancelado", 18m) },
            batches = Array.Empty<object>(), cashEntries = Array.Empty<object>(), cashClosings = Array.Empty<object>()
        };
        var json = JsonSerializer.Serialize(estado, Json);
        var request = new LegacySnapshotRequest(Guid.NewGuid(), json, Sha(json), ["mut_" + Guid.NewGuid()], true, empresa, loja, "Operacao");
        var contasAntes = await ReadAsync(empresa, async (db, _) => await db.Set<ContaReceber>().CountAsync(c => c.EmpresaId == empresa));

        var response = await fixture.CreateMobileClient(device.ApiKey).PostAsJsonAsync("/api/mobile/legacy-import", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var recibo = (await response.Content.ReadFromJsonAsync<LegacySnapshotResponse>())!;
        recibo.Warnings.Should().Contain(w => w.StartsWith("3 pedidos entregues entraram quitados na origem, sem pagamento nem caixa no ERP."),
            "o aviso diz quantos entregues entraram quitados, em vez de dizer que os pagamentos nao foram presumidos");
        recibo.Warnings.Should().NotContain(w => w.Contains("presumidos"));

        await ReadAsync(empresa, async (db, saldos) =>
        {
            var pedidos = await db.Set<Pedido>().ToListAsync();
            using var escopo = new AssertionScope();
            pedidos.Should().HaveCount(5);
            foreach (var id in entregues)
            {
                var p = pedidos.Single(x => x.MobileOrderId == id);
                p.Origem.Should().Be("mobile-import");
                p.QuitadoHistoricoEm.Should().NotBeNull($"o entregue {id} do histórico entra quitado, pela marca");
                p.QuitadoHistoricoEm.Should().Be(p.EntreguEm, "a marca guarda a data original da entrega, nunca a de hoje");
                (await AReceberDoPedidoMobile.CalcularAsync(p, saldos, CancellationToken.None))
                    .Should().Be(0m, $"o entregue {id} do histórico não pode ficar a receber");
            }
            var idsDosPedidos = pedidos.Select(p => p.Id).ToList();
            var eventos = await db.Set<PedidoEvento>().Where(e => e.Tipo == "importado" && idsDosPedidos.Contains(e.PedidoId)).ToListAsync();
            foreach (var id in entregues)
            {
                var evento = eventos.Single(e => e.PedidoId == pedidos.Single(x => x.MobileOrderId == id).Id);
                evento.Detalhes.Should().Contain("quitado na origem", $"o evento do entregue {id} diz que entrou quitado");
                evento.Detalhes.Should().NotContain("presumidos");
            }
            var ativo = pedidos.Single(x => x.MobileOrderId == aguardando);
            ativo.Origem.Should().Be("mobile");
            ativo.QuitadoHistoricoEm.Should().BeNull("o aguardando segue exatamente como antes");
            (await AReceberDoPedidoMobile.CalcularAsync(ativo, saldos, CancellationToken.None)).Should().Be(22m);
            var cancelado2 = pedidos.Single(x => x.MobileOrderId == cancelado);
            cancelado2.Origem.Should().Be("mobile-import");
            cancelado2.QuitadoHistoricoEm.Should().BeNull("o cancelado segue exatamente como antes");

            (await db.Set<PedidoPagamento>().Where(pg => db.Set<Pedido>().Any(o => o.Id == pg.PedidoId && o.EmpresaId == empresa)).CountAsync())
                .Should().Be(0, "nenhum pagamento é criado");
            (await db.Set<MovimentoCaixa>().CountAsync(m => m.EmpresaId == empresa)).Should().Be(0, "nenhum movimento de caixa por pedido do histórico");
            (await db.Set<ContaReceber>().CountAsync(c => c.EmpresaId == empresa)).Should().Be(contasAntes, "nenhuma conta a receber nova");
            (await db.Set<MovimentacaoEstoque>().CountAsync(m => m.EmpresaId == empresa && m.Natureza == NaturezaMovimentacaoEstoque.Venda))
                .Should().Be(0, "o import não grava saída de estoque para o pedido do histórico");
            return 0;
        });
    }

    [SkippableFact]
    public async Task CancelarEntregueDoHistorico_NaoMovimentaCaixaNemEstoque_EEstornoManualRecusaSemPagamento()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var device = await fixture.SeedMobileDeviceAsync(empresa, loja);
        var produto = "p-" + Guid.NewGuid().ToString("N");
        var cliente = "c-" + Guid.NewGuid().ToString("N");
        var entregue = "o-" + Guid.NewGuid().ToString("N");
        var quando = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        var estado = new
        {
            products = new[] { new { id = produto, name = "Produto " + produto, category = "massa", stock = 7, price = 10m, unit = "un" } },
            clients = new[] { new { id = cliente, name = "Cliente " + cliente, apt = "62", address = "Endereco", phone = "11999999999", lastOrder = quando, orderCount = 1 } },
            orders = new[] { new { id = entregue, clientId = cliente, clientSnapshot = new { name = "Cliente " + cliente },
                items = new[] { new { productId = produto, name = "Produto " + produto, qty = 1, unitPrice = 30m } },
                total = 30m, status = "entregue", createdAt = quando, updatedAt = quando } },
            batches = Array.Empty<object>(), cashEntries = Array.Empty<object>(), cashClosings = Array.Empty<object>()
        };
        var json = JsonSerializer.Serialize(estado, Json);
        var request = new LegacySnapshotRequest(Guid.NewGuid(), json, Sha(json), ["mut_" + Guid.NewGuid()], true, empresa, loja, "Operacao");
        var importacao = await fixture.CreateMobileClient(device.ApiKey).PostAsJsonAsync("/api/mobile/legacy-import", request);
        importacao.StatusCode.Should().Be(HttpStatusCode.OK, await importacao.Content.ReadAsStringAsync());

        var pedidoId = await ReadAsync(empresa, async (db, _) =>
        {
            var pedido = await db.Set<Pedido>().SingleAsync(p => p.MobileOrderId == entregue);
            pedido.QuitadoHistoricoEm.Should().NotBeNull("pré-condição: o entregue do histórico entra quitado, pela marca");
            return pedido.Id;
        });
        var antes = await ContarAsync(empresa);

        var gerente = await ClienteGerenteAsync(empresa);
        var cancelar = await gerente.PostAsJsonAsync($"/api/pedidos/{pedidoId}/cancelar",
            new { empresaId = empresa, motivo = "Cancelado depois da importação" });
        cancelar.StatusCode.Should().Be(HttpStatusCode.OK, await cancelar.Content.ReadAsStringAsync());

        var estorno = await gerente.PostAsJsonAsync($"/api/pedidos/{pedidoId}/estornos-manuais?empresaId={empresa}",
            new { operacaoId = Guid.NewGuid(), pagamentoId = Guid.NewGuid(), valor = 10m, metodo = "dinheiro",
                motivo = "Cliente pediu", referencia = (string?)null, valorJaDevolvido = true });
        estorno.StatusCode.Should().Be(HttpStatusCode.Conflict, await estorno.Content.ReadAsStringAsync());
        (await estorno.Content.ReadAsStringAsync()).Should().Contain("sem_pagamento");

        await ReadAsync(empresa, async (db, _) =>
        {
            using var escopo = new AssertionScope();
            var pedido = await db.Set<Pedido>().Include(p => p.Eventos).SingleAsync(p => p.Id == pedidoId);
            pedido.Status.Should().Be(StatusPedidoMapper.Cancelado);
            pedido.Pagamentos.Should().BeEmpty("cancelar não cria pagamento nem estorno");
            pedido.Eventos.Should().NotContain(e => e.Tipo == CancelarPedidoUseCase.EventoDevolucaoAFazer,
                "o entregue do histórico não tem o que devolver");
            return 0;
        });
        (await ContarAsync(empresa)).Should().Be(antes, "cancelar e recusar o estorno não movimentam caixa, estoque nem contas a receber");
    }

    private async Task<(int Caixa, int Estoque, int Contas)> ContarAsync(Guid empresa) =>
        await ReadAsync(empresa, async (db, _) => (
            await db.Set<MovimentoCaixa>().CountAsync(m => m.EmpresaId == empresa),
            await db.Set<MovimentacaoEstoque>().CountAsync(m => m.EmpresaId == empresa),
            await db.Set<ContaReceber>().CountAsync(c => c.EmpresaId == empresa)));

    private async Task<HttpClient> ClienteGerenteAsync(Guid empresa)
    {
        Guid usuarioId;
        using (var scope = fixture.Factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            using var bypass = db.UseRowLevelSecurityBypass();
            var usuario = Usuario.Criar("Teste Gerente", $"historico-gerente-{Guid.NewGuid():N}@example.test", "sem-login-por-senha");
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            usuarioId = usuario.Id;
        }

        var claims = new List<Claim>
        {
            new("sub", usuarioId.ToString()),
            new("nivel", NivelAcesso.Gerente.ToString()),
            new("empresaId", empresa.ToString()),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("EasyStock-Test-SuperSecretKey-Min32Chars!!"));
        var jwt = new JwtSecurityToken("EasyStock", "EasyStock", claims, expires: DateTime.UtcNow.AddMinutes(20),
            signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));
        var cliente = fixture.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        return cliente;
    }

    private async Task<T> ReadAsync<T>(Guid empresa, Func<EasyStockDbContext, SaldoFinanceiroPedido, Task<T>> action)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(empresa);
        return await action(db, scope.ServiceProvider.GetRequiredService<SaldoFinanceiroPedido>());
    }

    private static string Sha(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
