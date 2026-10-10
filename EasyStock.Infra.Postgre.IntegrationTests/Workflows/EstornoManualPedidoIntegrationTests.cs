using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Pagamentos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

[Collection("PostgreSqlTestCollection")]
public sealed class EstornoManualPedidoIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Agora = new(2026, 10, 9, 17, 0, 0, DateTimeKind.Utc);
    private sealed class Relogio : TimeProvider { public override DateTimeOffset GetUtcNow() => Agora; }

    private async Task<RegistrarEstornoManualInput> Preparar(bool venda = false)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        await using var db = fixture.CreateDbContext();
        var empresa = new Empresa { Id = Guid.NewGuid(), Nome = "Devolução teste", Documento = Guid.NewGuid().ToString("N")[..14], CriadoEm = Agora, AlteradoEm = Agora };
        db.Empresas.Add(empresa);
        db.SetMobileTenantContext(empresa.Id);
        var pedido = Pedido.Criar(empresa.Id);
        pedido.Status = "cancelado";
        var pagamento = new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Valor = 100, Metodo = "dinheiro", PagoEm = Agora.AddDays(-1) };
        pedido.Pagamentos.Add(pagamento);
        if (venda)
        {
            var consolidada = new Venda { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Canal = CanalVenda.LojaPropria,
                Natureza = NaturezaMovimentacaoEstoque.Venda, DataVenda = pagamento.PagoEm,
                ValorTotal = Dinheiro.FromDecimal(100), CriadoEm = pagamento.PagoEm };
            db.Vendas.Add(consolidada);
            pedido.VendaId = consolidada.Id;
        }
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return new(empresa.Id, pedido.Id, Guid.NewGuid(), pagamento.Id, 40, "pix", "Desistência", "Pix-teste-123", true,
            Guid.NewGuid(), "Dona teste", NivelAcesso.Admin);
    }

    private static RegistrarEstornoManualUseCase UseCase(EasyStockDbContext db) =>
        new(new PedidoRepository(db), new CobrancaPedidoRepository(db), new CaixaRepository(db), db, new Relogio());

    private async Task<PedidoEstornoManual> Executar(RegistrarEstornoManualInput input)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        return await UseCase(db).ExecuteAsync(input);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parciais_preservam_recebimento_e_descontam_caixa_na_data_da_devolucao(bool venda)
    {
        var input = await Preparar(venda);
        var primeiro = await Executar(input);
        var replay = await Executar(input);
        replay.Id.Should().Be(primeiro.Id);
        await Executar(input with { OperacaoId = Guid.NewGuid(), Valor = 60 });
        await FluentActions.Invoking(() => Executar(input with { OperacaoId = Guid.NewGuid(), Valor = 0.01m }))
            .Should().ThrowAsync<CobrancaPedidoConflitoException>();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        var caixa = new CaixaSaldoCalculator(new CaixaRepository(db));
        var ontem = await caixa.CalcularAsync(input.EmpresaId, new DateOnly(2026, 10, 8));
        var hoje = await caixa.CalcularAsync(input.EmpresaId, new DateOnly(2026, 10, 9));
        ontem.SaldoEsperado.Should().Be(100);
        hoje.TotalSaidas.Should().Be(100);
        hoje.SaldoEsperado.Should().Be(-100);
        var salvo = await db.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == input.PedidoId);
        salvo.TotalPago.Should().Be(100);
        salvo.Status.Should().Be("cancelado");
        (await db.Set<PedidoEstornoManual>().CountAsync()).Should().Be(2);
        (await db.MovimentosCaixa.CountAsync(m => m.Origem == PedidoEstornoManual.OrigemCaixa)).Should().Be(2);
        (await db.Set<PedidoEvento>().CountAsync(e => e.PedidoId == input.PedidoId && e.Tipo == "devolucao_manual")).Should().Be(2);
        var consulta = await new ConsultarEstornosManuaisUseCase(new PedidoRepository(db), new CobrancaPedidoRepository(db))
            .ExecuteAsync(input.EmpresaId, input.PedidoId);
        consulta.Pagamentos.Single().Disponivel.Should().Be(0);
        consulta.Pagamentos.Single().Devolvido.Should().Be(100);
        consulta.Estornos.Should().OnlyContain(e => e.Metodo == "pix" && e.UsuarioId == input.UsuarioId && e.Referencia == input.Referencia);
    }

    [SkippableFact]
    public async Task Concorrencia_limita_o_total_e_replay_tem_uma_unica_saida()
    {
        var input = (await Preparar()) with { Valor = 70 };
        async Task<bool> Tentar(RegistrarEstornoManualInput cmd)
        {
            try { await Executar(cmd); return true; }
            catch (CobrancaPedidoConflitoException) { return false; }
        }
        var disputa = await Task.WhenAll(Tentar(input), Tentar(input with { OperacaoId = Guid.NewGuid() }));
        disputa.Count(ok => ok).Should().Be(1);
        var outro = (await Preparar()) with { Valor = 70 };
        var iguais = await Task.WhenAll(Executar(outro), Executar(outro));
        iguais.Select(e => e.Id).Distinct().Should().ContainSingle();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(outro.EmpresaId);
        (await db.MovimentosCaixa.Where(m => m.Origem == PedidoEstornoManual.OrigemCaixa).SumAsync(m => m.Valor)).Should().Be(70);
    }

    [SkippableFact]
    public async Task Falha_no_commit_reverte_saida_devolucao_e_evento()
    {
        var input = await Preparar();
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(input.EmpresaId);
            // FK inválida falha no mesmo SaveChanges, depois de todas as inclusões do caso de uso.
            db.Set<PedidoEvento>().Add(new PedidoEvento { Id = Guid.NewGuid(), PedidoId = Guid.NewGuid(), Tipo = "teste", OcorridoEm = Agora });
            await FluentActions.Invoking(() => UseCase(db).ExecuteAsync(input)).Should().ThrowAsync<DbUpdateException>();
        }
        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(input.EmpresaId);
        (await conferir.Set<PedidoEstornoManual>().CountAsync()).Should().Be(0);
        (await conferir.MovimentosCaixa.CountAsync()).Should().Be(0);
        (await conferir.Set<PedidoEvento>().CountAsync(e => e.PedidoId == input.PedidoId)).Should().Be(0);
        // A mesma confirmação pode ser reenviada após a falha.
        await Executar(input);
    }

    [SkippableFact]
    public async Task Bloqueia_perfil_valores_sem_confirmacao_caixa_fechado_e_reuso_alterado()
    {
        var input = await Preparar();
        await FluentActions.Invoking(() => Executar(input with { NivelSolicitante = NivelAcesso.Operador }))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        foreach (var invalido in new[] { input with { Valor = 0 }, input with { Valor = -1 }, input with { Valor = 1.001m },
                     input with { ValorJaDevolvido = false }, input with { Motivo = " " }, input with { Referencia = " " }, input with { Metodo = "inexistente" } })
            await FluentActions.Invoking(() => Executar(invalido)).Should().ThrowAsync<UseCaseValidationException>();
        await Executar(input);
        await FluentActions.Invoking(() => Executar(input with { Valor = 41 })).Should().ThrowAsync<CobrancaPedidoConflitoException>();
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(input.EmpresaId);
            db.FechamentosCaixa.Add(FechamentoCaixa.Criar(input.EmpresaId, new DateOnly(2026, 10, 9), 0, 0, 0, 0, 40));
            await db.SaveChangesAsync();
        }
        await Executar(input); // Repetir confirmação anterior permanece seguro após fechar o Caixa.
        await FluentActions.Invoking(() => Executar(input with { OperacaoId = Guid.NewGuid() })).Should().ThrowAsync<UseCaseValidationException>();
    }

    [SkippableFact]
    public async Task Pedido_pagamento_e_leitura_respeitam_empresa_e_provedor()
    {
        var input = await Preparar();
        var outro = await Preparar();
        await FluentActions.Invoking(() => Executar(input with { EmpresaId = outro.EmpresaId }))
            .Should().ThrowAsync<CobrancaPedidoNaoEncontradoException>();
        await FluentActions.Invoking(() => Executar(input with { PagamentoId = outro.PagamentoId }))
            .Should().ThrowAsync<CobrancaPedidoConflitoException>();
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(input.EmpresaId);
            var pagamento = await db.Set<PedidoPagamento>().SingleAsync(p => p.Id == input.PagamentoId);
            pagamento.Referencia = "123456789";
            var cobranca = CobrancaPedido.CriarOnline(input.EmpresaId, input.PedidoId, 100, "pref-teste", "https://mp.test/pref", Agora.AddHours(1), 1, Agora);
            cobranca.MarcarPaga("123456789", 100, "pix", Agora);
            db.Set<CobrancaPedido>().Add(cobranca);
            await db.SaveChangesAsync();
            var resumo = await new ConsultarEstornosManuaisUseCase(new PedidoRepository(db), new CobrancaPedidoRepository(db)).ExecuteAsync(input.EmpresaId, input.PedidoId);
            resumo.Pagamentos.Single().Manual.Should().BeFalse();
            resumo.Pagamentos.Single().Disponivel.Should().Be(0);
        }
        await FluentActions.Invoking(() => Executar(input)).Should().ThrowAsync<CobrancaPedidoConflitoException>();
        await Executar(outro);
        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(input.EmpresaId);
        (await leitura.Set<PedidoEstornoManual>().ToListAsync()).Should().BeEmpty();
        (await new PedidoRepository(leitura).ListarEstornosManuaisAsync(input.EmpresaId, outro.PedidoId)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Devolucao_confirmada_nao_pode_ser_apagada_pelo_caixa_ou_pelo_recebimento()
    {
        var input = await Preparar();
        await Executar(input);
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(input.EmpresaId);
        var uc = new EstornarMovimentoCaixaUseCase(new CaixaRepository(db), db, NullLogger<EstornarMovimentoCaixaUseCase>.Instance);
        await FluentActions.Invoking(() => uc.ExecuteAsync(new(input.EmpresaId, input.OperacaoId, "Apagar")))
            .Should().ThrowAsync<UseCaseValidationException>();
        db.Set<PedidoPagamento>().Remove(await db.Set<PedidoPagamento>().SingleAsync(p => p.Id == input.PagamentoId));
        await FluentActions.Invoking(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [SkippableFact]
    public async Task Rls_impede_leitura_e_escrita_de_outra_empresa_mesmo_sem_filtro_ef()
    {
        var input = await Preparar();
        var outro = await Preparar();
        await Executar(input);
        await using var db = fixture.CreateRlsClientDbContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {outro.EmpresaId.ToString()}, false)");
        (await db.Set<PedidoEstornoManual>().IgnoreQueryFilters().CountAsync()).Should().Be(0);
        await FluentActions.Invoking(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO pedido_estornos_manuais ("Id", "EmpresaId", "PedidoId", "PagamentoId", "Valor", "Metodo", "Motivo", "Referencia", "UsuarioId", "RegistradoEm")
            VALUES ({Guid.NewGuid()}, {input.EmpresaId}, {input.PedidoId}, {input.PagamentoId}, 1, 'pix', 'teste', 'teste', {input.UsuarioId}, now())
            """)).Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.SqlState == "42501");
    }
}
