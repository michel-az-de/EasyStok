using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// E4 L3 (#3076): a linha "Pedidos a receber" do dashboard financeiro soma, no banco, o saldo dos pedidos operacionais
/// sem conta a receber ativa, com o dia da entrega (AgendadoParaEm ?? CriadoEm) antes da meia-noite de Brasília de hoje+31.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class PedidosAReceberKpiIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    // Hoje é 10/06/2026 em Brasília (a referência é a data civil 00:00Z). O corte é 11/07 03:00Z (meia-noite de hoje+31).
    private static readonly DateTime Referencia = new(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime Utc(int mes, int dia, int hora = 12, int min = 0) => new(2026, mes, dia, hora, min, 0, DateTimeKind.Utc);

    private sealed class Cenario(EasyStockDbContext db, Guid empresa, Guid categoria) : IAsyncDisposable
    {
        public EasyStockDbContext Db { get; } = db;
        public Guid Empresa { get; } = empresa;
        public Guid Categoria { get; } = categoria;
        public ValueTask DisposeAsync() => Db.DisposeAsync();

        public Pedido Pedido(string status, decimal total, DateTime? agendado = null, DateTime? criadoEm = null,
            decimal pago = 0m, string origem = "whatsapp", Guid? empresa = null)
        {
            var p = EasyStock.Domain.Entities.Pedido.Criar(empresa ?? Empresa, origem: origem);
            p.Status = status;
            p.Total = Dinheiro.FromDecimal(total);
            p.CriadoEm = criadoEm ?? Utc(6, 10, 15);
            p.AgendadoParaEm = agendado;
            if (pago > 0m)
                p.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = p.Id, Metodo = "pix", Valor = pago,
                    Referencia = Guid.NewGuid().ToString("N")[..24], PagoEm = Utc(6, 10, 15) });
            Db.Pedidos.Add(p);
            return p;
        }

        public ContaReceber Conta(decimal valor, DateTime vencimento, Guid? pedidoId = null, bool cancelada = false, bool paga = false)
        {
            var c = ContaReceber.Criar(Empresa, null, Categoria, "Conta", Utc(6, 1, 0),
                origem: pedidoId.HasValue ? OrigemContaFinanceira.Pedido : OrigemContaFinanceira.Manual, origemRefId: pedidoId);
            c.AdicionarParcela(1, valor, vencimento);
            c.Emitir();
            if (cancelada) c.Cancelar("teste", null);
            if (paga) c.Status = StatusContaFinanceira.Paga;
            Db.ContasReceber.Add(c);
            return c;
        }

        public async Task<DashboardFinanceiroDto> KpisAsync()
        {
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return await new FluxoCaixaQueries(Db).KpisDashboardAsync(Empresa, Referencia);
        }
    }

    private async Task<Cenario> PrepararAsync()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var empresa = Guid.NewGuid();
        var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        db.Empresas.Add(NovaEmpresa(empresa));
        var categoria = CategoriaFinanceira.Criar(empresa, "Vendas", TipoCategoriaFinanceira.Receita);
        db.Add(categoria);
        return new Cenario(db, empresa, categoria.Id);
    }

    private static Empresa NovaEmpresa(Guid id) => new()
    {
        Id = id, Nome = "Pedidos a receber", Documento = id.ToString("N")[..14],
        CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
    };

    [SkippableFact]
    public async Task Conta_de_120_fica_no_card_e_pedido_de_132_30_sem_conta_vai_para_a_linha_sem_somar_outra_empresa()
    {
        await using var c = await PrepararAsync();
        var outra = Guid.NewGuid();
        c.Db.Empresas.Add(NovaEmpresa(outra));
        c.Conta(120m, Utc(6, 20, 0));
        c.Pedido("preparando", 132.30m, agendado: Utc(6, 15, 15));
        c.Pedido("preparando", 500m, agendado: Utc(6, 15, 15), empresa: outra);

        var k = await c.KpisAsync();

        k.TotalAVencer30dReceber.Should().Be(120m);
        k.TotalPedidosAReceber.Should().Be(132.30m);
        k.QtdPedidosAReceber.Should().Be(1);
    }

    [SkippableFact]
    public async Task Conta_gerada_do_pedido_entra_no_card_e_tira_o_pedido_da_linha()
    {
        await using var c = await PrepararAsync();
        c.Conta(120m, Utc(6, 20, 0));
        var pedido = c.Pedido("entregue", 132.30m, agendado: Utc(6, 12, 15));
        c.Conta(132.30m, Utc(6, 17, 0), pedido.Id);

        var k = await c.KpisAsync();

        k.TotalAVencer30dReceber.Should().Be(252.30m);
        k.TotalPedidosAReceber.Should().Be(0m);
        k.QtdPedidosAReceber.Should().Be(0);
    }

    [SkippableFact]
    public async Task Exclui_cancelado_pre_operacional_pago_quitado_do_historico_entrega_a_partir_de_hoje_mais_31_e_conta_nao_cancelada()
    {
        await using var c = await PrepararAsync();
        c.Pedido("aguardando", 10m, agendado: Utc(6, 12, 15));
        c.Pedido("cancelado", 20m, agendado: Utc(6, 12, 15));
        c.Pedido("rascunho", 30m, agendado: Utc(6, 12, 15));
        c.Pedido("aguardando_pagamento", 40m, agendado: Utc(6, 12, 15));
        c.Pedido("aguardando_aprovacao_baba", 50m, agendado: Utc(6, 12, 15));
        c.Pedido("preparando", 60m, agendado: Utc(6, 12, 15), pago: 60m);
        c.Pedido("preparando", 65m, agendado: Utc(6, 12, 15), pago: 70m);
        c.Pedido("preparando", 0m, agendado: Utc(6, 12, 15));
        var quitado = c.Pedido("entregue", 80m, agendado: Utc(6, 12, 15));
        quitado.MarcarQuitadoHistorico(Utc(6, 12, 16));
        c.Pedido("preparando", 90m, agendado: Utc(7, 11, 3)); // exatamente hoje+31 às 00:00 de Brasília: fora
        var comConta = c.Pedido("preparando", 100m, agendado: Utc(6, 12, 15));
        c.Conta(100m, Utc(6, 17, 0), comConta.Id);
        var comContaPaga = c.Pedido("entregue", 110m, agendado: Utc(6, 12, 15));
        c.Conta(110m, Utc(6, 17, 0), comContaPaga.Id, paga: true);

        var k = await c.KpisAsync();

        k.QtdPedidosAReceber.Should().Be(1, "só o aguardando de R$ 10 cumpre todos os critérios");
        k.TotalPedidosAReceber.Should().Be(10m);
    }

    [SkippableFact]
    public async Task Pagamento_parcial_entra_so_com_o_saldo()
    {
        await using var c = await PrepararAsync();
        c.Pedido("preparando", 100m, agendado: Utc(6, 12, 15), pago: 30m);

        var k = await c.KpisAsync();

        k.TotalPedidosAReceber.Should().Be(70m);
        k.QtdPedidosAReceber.Should().Be(1);
    }

    [SkippableFact]
    public async Task Pedido_com_conta_cancelada_volta_para_a_linha()
    {
        await using var c = await PrepararAsync();
        var pedido = c.Pedido("entregue", 80m, agendado: Utc(6, 12, 15));
        c.Conta(80m, Utc(6, 17, 0), pedido.Id, cancelada: true);

        var k = await c.KpisAsync();

        k.TotalPedidosAReceber.Should().Be(80m);
        k.QtdPedidosAReceber.Should().Be(1);
        k.TotalAVencer30dReceber.Should().Be(0m);
    }

    [SkippableFact]
    public async Task Borda_de_fuso_21h_de_Brasilia_de_hoje_mais_30_entra_e_a_de_hoje_mais_31_nao()
    {
        await using var c = await PrepararAsync();
        c.Pedido("preparando", 10m, agendado: Utc(7, 11, 0));  // 10/07 21:00 em Brasília (hoje+30): entra
        c.Pedido("preparando", 20m, agendado: Utc(7, 12, 0));  // 11/07 21:00 em Brasília (hoje+31): fora

        var k = await c.KpisAsync();

        k.TotalPedidosAReceber.Should().Be(10m);
        k.QtdPedidosAReceber.Should().Be(1);
    }

    [SkippableFact]
    public async Task Pedido_entregue_ha_45_dias_sem_conta_nem_pagamento_entra_e_o_card_das_contas_nao_muda()
    {
        await using var c = await PrepararAsync();
        c.Pedido("entregue", 80m, agendado: Utc(4, 26, 15), criadoEm: Utc(4, 20, 15));

        var k = await c.KpisAsync();

        k.TotalPedidosAReceber.Should().Be(80m);
        k.QtdPedidosAReceber.Should().Be(1);
        k.TotalAVencer30dReceber.Should().Be(0m);
        k.TotalVencidoReceber.Should().Be(0m);
    }

    [SkippableFact]
    public async Task Pedido_importado_do_historico_pronto_sem_pagamento_entra_com_o_total_cheio()
    {
        await using var c = await PrepararAsync();
        c.Pedido("pronto", 75m, origem: "mobile-import", criadoEm: Utc(5, 1, 15));

        var k = await c.KpisAsync();

        k.TotalPedidosAReceber.Should().Be(75m);
        k.QtdPedidosAReceber.Should().Be(1);
    }

    [SkippableFact]
    public async Task Limite_declarado_pedido_de_100_com_cupom_de_20_e_80_pagos_entra_com_20()
    {
        // O cupom/crédito do cliente não é um PedidoPagamento: o saldo da linha é Total menos pagamentos, então o
        // cupom de R$ 20 segue contando como a receber. Fixa o limite declarado na spec E4 (gravar o cupom não
        // muda o cálculo; o pedido de R$ 100 só tem os R$ 80 pagos como pagamento).
        await using var c = await PrepararAsync();
        c.Pedido("preparando", 100m, agendado: Utc(6, 12, 15), pago: 80m);

        var k = await c.KpisAsync();

        k.TotalPedidosAReceber.Should().Be(20m);
        k.QtdPedidosAReceber.Should().Be(1);
    }
}
