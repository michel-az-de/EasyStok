using System.Data.Common;
using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Operacao.Kds;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Pagamentos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Operacao;

[Collection("PostgreSqlTestCollection")]
public sealed class KdsSaldoLotePostgresTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Uma_e_duzentas_comandas_usam_a_mesma_quantidade_de_consultas_e_preservam_filtro_de_linha()
    {
        Disponivel();
        var empresa = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.SetMobileTenantContext(empresa);
        seed.Empresas.Add(NovaEmpresa(empresa));
        var primeiro = NovoPedido(empresa);
        seed.Pedidos.Add(primeiro);
        await seed.SaveChangesAsync();

        var contador = new ContadorConsultas();
        await using var leitura = Contexto(contador);
        leitura.SetMobileTenantContext(empresa);
        var queries = new KdsPedidoQueries(leitura);
        var hoje = HorarioBrasil.Hoje();
        (await queries.ListarAsync(empresa, [StatusPedidoMapper.Aguardando], hoje, hoje)).Should().ContainSingle();
        var uma = contador.Total;
        uma.Should().Be(9, "três leituras do agregado, uma de janelas e cinco totais financeiros em lote");

        for (var i = 1; i < 200; i++) seed.Pedidos.Add(NovoPedido(empresa, linha: "paraServir"));
        await seed.SaveChangesAsync();
        contador.Total = 0;
        var pagina = await queries.ListarAsync(empresa, [StatusPedidoMapper.Aguardando], hoje, hoje);
        pagina.Should().HaveCount(200);
        pagina.Should().OnlyContain(p => p.AReceber == 100m);
        contador.Total.Should().Be(uma);

        contador.Total = 0;
        var cards = await new ListarPedidosKdsUseCase(queries, TimeProvider.System)
            .ExecuteAsync(new(empresa, Linha: "preparar_em_casa", Data: hoje));
        cards.Should().ContainSingle().Which.Id.Should().Be(primeiro.Id);
        cards.Single().AReceber.Should().Be(100m);
        contador.Total.Should().Be(uma, "o mapeamento e o filtro de linha não fazem consultas por cartão");
    }

    [SkippableFact]
    public async Task Lote_equivale_ao_saldo_individual_com_legado_estornos_creditos_e_isolamento_explicito()
    {
        Disponivel();
        var empresa = Guid.NewGuid();
        var outra = Guid.NewGuid();
        // SuperAdmin ignora filtros globais: a prova depende dos WHERE EmpresaId da leitura.
        await using var db = Contexto(superAdmin: true);
        db.Empresas.AddRange(NovaEmpresa(empresa), NovaEmpresa(outra));
        var cliente = Cliente.Criar(empresa, "Cliente do saldo em lote");
        db.Clientes.Add(cliente);
        var pedido = NovoPedido(empresa);
        var origem = NovoPedido(empresa);
        origem.Status = StatusPedidoMapper.Cancelado;
        var estrangeiro = NovoPedido(outra);
        db.Pedidos.AddRange(pedido, origem, estrangeiro);
        var basePagamento = Pagar(pedido, 50m);
        var legado = Pagar(pedido, 10m);
        var confirmado = Pagar(pedido, 20m);
        var pendente = Pagar(pedido, 10m);
        var recusado = Pagar(pedido, 10m);
        foreach (var p in new[] { legado, confirmado, pendente, recusado }) CobrancaEstornada(db, empresa, pedido, p);
        Online(db, empresa, pedido, confirmado, 7m, PedidoEstornoOnline.Confirmado);
        Online(db, empresa, pedido, pendente, 5m, PedidoEstornoOnline.Pendente);
        Online(db, empresa, pedido, recusado, 2m, PedidoEstornoOnline.Recusado);
        Manual(db, empresa, pedido, basePagamento, 3m);
        db.Set<CreditoClienteComanda>().Add(CreditoClienteComanda.Criar(empresa, cliente.Id, pedido.Id, Guid.NewGuid(), 4m, DateTime.UtcNow));
        var usado = CreditoClienteComanda.Criar(empresa, cliente.Id, origem.Id, Guid.NewGuid(), 6m, DateTime.UtcNow);
        usado.Usar(pedido.Id, 6m, DateTime.UtcNow);
        db.Set<CreditoClienteComanda>().Add(usado);

        // Linhas estrangeiras referindo os mesmos IDs não podem alterar nenhuma parcela.
        Online(db, outra, pedido, confirmado, 40m, PedidoEstornoOnline.Confirmado);
        Online(db, outra, pedido, legado, 40m, PedidoEstornoOnline.Pendente);
        Manual(db, outra, pedido, basePagamento, 40m);
        CobrancaEstornada(db, outra, pedido, basePagamento);
        var creditoAlheio = CreditoClienteComanda.Criar(outra, cliente.Id, pedido.Id, Guid.NewGuid(), 40m, DateTime.UtcNow);
        creditoAlheio.Usar(pedido.Id, 20m, DateTime.UtcNow);
        db.Set<CreditoClienteComanda>().Add(creditoAlheio);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var saldos = new SaldoFinanceiroPedido(new PedidoRepository(db), new EstornoOnlineRepository(db), new CreditoClienteComandaRepository(db));
        var saldo = await saldos.ConsultarAsync(empresa, pedido.Id, 100m, CancellationToken.None);
        saldo.Should().Be(new SaldoFinanceiroComanda(100m, 20m, 4m, 6m, 5m));
        var hoje = HorarioBrasil.Hoje();
        var lote = await new KdsPedidoQueries(db).ListarAsync(empresa, [StatusPedidoMapper.Aguardando], hoje, hoje);
        var lido = lote.Should().ContainSingle().Which;
        lido.Id.Should().Be(pedido.Id);
        lido.AReceber.Should().Be(18m).And.Be(await saldos.AReceberAsync(empresa, pedido.Id, 100m, 100m, CancellationToken.None));
        var outroLote = await new KdsPedidoQueries(db).ListarAsync(outra, [StatusPedidoMapper.Aguardando], hoje, hoje);
        outroLote.Should().ContainSingle().Which.Id.Should().Be(estrangeiro.Id);
        outroLote.Single().AReceber.Should().Be(100m);
    }

    [SkippableFact]
    public async Task Saldo_zero_pagamento_excedente_e_credito_aplicado_conservam_equivalencia()
    {
        Disponivel();
        var empresa = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        db.Empresas.Add(NovaEmpresa(empresa));
        var cliente = Cliente.Criar(empresa, "Cliente do cupom");
        db.Clientes.Add(cliente);
        var semTotal = NovoPedido(empresa, total: 0m);
        var excedente = NovoPedido(empresa);
        var cupom = NovoPedido(empresa);
        var origem = NovoPedido(empresa);
        origem.Status = StatusPedidoMapper.Cancelado;
        db.Pedidos.AddRange(semTotal, excedente, cupom, origem);
        Manual(db, empresa, semTotal, Pagar(semTotal, 1m), 2m);
        Pagar(excedente, 120m);
        Pagar(cupom, 25m);
        var credito = CreditoClienteComanda.Criar(empresa, cliente.Id, origem.Id, Guid.NewGuid(), 75m, DateTime.UtcNow);
        credito.Usar(cupom.Id, 75m, DateTime.UtcNow);
        db.Set<CreditoClienteComanda>().Add(credito);
        await db.SaveChangesAsync();
        var saldos = new SaldoFinanceiroPedido(new PedidoRepository(db), new EstornoOnlineRepository(db), new CreditoClienteComandaRepository(db));
        var hoje = HorarioBrasil.Hoje();
        var lote = await new KdsPedidoQueries(db).ListarAsync(empresa, [StatusPedidoMapper.Aguardando], hoje, hoje);
        lote.Should().HaveCount(3);
        foreach (var pedido in new[] { semTotal, excedente, cupom })
            lote.Single(p => p.Id == pedido.Id).AReceber.Should().Be(0m)
                .And.Be(await saldos.AReceberAsync(pedido, CancellationToken.None));
    }

    private void Disponivel() => Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");

    private EasyStockDbContext Contexto(ContadorConsultas? contador = null, bool superAdmin = false)
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(fixture.ConnectionString);
        if (contador is not null) options.AddInterceptors(contador);
        var user = Substitute.For<ICurrentUserAccessor>();
        user.IsAuthenticated.Returns(superAdmin);
        user.Nivel.Returns(superAdmin ? NivelAcesso.SuperAdmin : NivelAcesso.Operador);
        return new(options.Options, user);
    }

    private static Empresa NovaEmpresa(Guid id) => new()
    { Id = id, Nome = "Empresa do saldo KDS", Documento = id.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };

    private static Pedido NovoPedido(Guid empresa, decimal total = 100m, string linha = "prepararEmCasa")
    {
        var pedido = Pedido.Criar(empresa);
        pedido.Status = StatusPedidoMapper.Aguardando;
        pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Lasanha", Quantidade = 1,
            PrecoUnitario = total, Subtotal = total, LinhaSnapshot = linha, CriadoEm = DateTime.UtcNow });
        pedido.RecalcularTotal();
        return pedido;
    }

    private static PedidoPagamento Pagar(Pedido pedido, decimal valor)
    {
        var p = new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Valor = valor, Metodo = "pix",
            Referencia = Guid.NewGuid().ToString("N")[..24], PagoEm = DateTime.UtcNow };
        pedido.Pagamentos.Add(p);
        return p;
    }

    private static void CobrancaEstornada(EasyStockDbContext db, Guid empresa, Pedido pedido, PedidoPagamento p)
    {
        var agora = DateTime.UtcNow;
        var c = CobrancaPedido.CriarOnline(empresa, pedido.Id, p.Valor, $"pref-{Guid.NewGuid():N}", "https://mp.test/nao-enviar",
            agora.AddHours(1), 1, agora);
        c.MarcarPaga(p.Referencia!, p.Valor, p.Metodo, agora);
        c.MarcarEstornada("Fixture local", agora);
        db.CobrancasPedido.Add(c);
    }

    private static void Online(EasyStockDbContext db, Guid empresa, Pedido pedido, PedidoPagamento p, decimal valor, string situacao)
    {
        var movimento = situacao == PedidoEstornoOnline.Confirmado ? MovimentoCaixa.Criar(empresa, "saida", valor) : null;
        if (movimento is not null) db.Set<MovimentoCaixa>().Add(movimento);
        db.Set<PedidoEstornoOnline>().Add(new() { Id = Guid.NewGuid(), EmpresaId = empresa, PedidoId = pedido.Id,
            PagamentoId = p.Id, PagamentoExternoId = p.Referencia!, Valor = valor, Motivo = "Fixture local", Situacao = situacao,
            CriadoEm = DateTime.UtcNow, ConfirmadoEm = movimento is null ? null : DateTime.UtcNow, MovimentoCaixaId = movimento?.Id });
    }

    private static void Manual(EasyStockDbContext db, Guid empresa, Pedido pedido, PedidoPagamento p, decimal valor)
    {
        var movimento = MovimentoCaixa.Criar(empresa, "saida", valor);
        db.Set<MovimentoCaixa>().Add(movimento);
        db.Set<PedidoEstornoManual>().Add(new() { Id = movimento.Id, EmpresaId = empresa, PedidoId = pedido.Id, PagamentoId = p.Id,
            Valor = valor, Metodo = "pix", Motivo = "Fixture local", Referencia = "teste", UsuarioId = Guid.NewGuid(), RegistradoEm = DateTime.UtcNow });
    }

    private sealed class ContadorConsultas : DbCommandInterceptor
    {
        public int Total { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Total++;
            return ValueTask.FromResult(result);
        }
    }
}
