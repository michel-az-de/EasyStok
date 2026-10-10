using EasyStock.Application.Common;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class ResumoDiaPixTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pix_do_dia_inclui_recebimento_antes_da_abertura_e_filtra_data_metodo_e_empresa(bool abreHoje)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var outraEmpresa = Guid.NewGuid();
        db.Empresas.AddRange(
            new Empresa { Id = empresaId, Nome = "Pix do dia", Documento = empresaId.ToString("N")[..14] },
            new Empresa { Id = outraEmpresa, Nome = "Outra empresa", Documento = outraEmpresa.ToString("N")[..14] });
        var (inicio, fim) = HorarioBrasil.JanelaDiaUtc();
        db.SetMobileTenantContext(empresaId);
        db.MovimentosCaixa.Add(new MovimentoCaixa
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, Tipo = "abertura", Valor = 0,
            DataMovimento = abreHoje ? inicio.AddHours(2) : inicio.AddDays(-1)
        });
        var pedido = Pedido.Criar(empresaId);
        pedido.Total = Dinheiro.FromDecimal(100m);
        pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = 10m, PagoEm = inicio.AddHours(1) });
        pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = 20m, PagoEm = inicio.AddHours(3) });
        pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = 99m, PagoEm = inicio.AddSeconds(-1) });
        pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = 99m, PagoEm = fim });
        pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "dinheiro", Valor = 99m, PagoEm = inicio.AddHours(1) });
        var pedidoOutraEmpresa = Pedido.Criar(outraEmpresa);
        pedidoOutraEmpresa.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedidoOutraEmpresa.Id, Metodo = "pix", Valor = 999m, PagoEm = inicio.AddHours(1) });
        db.Pedidos.AddRange(pedido, pedidoOutraEmpresa);
        await db.SaveChangesAsync();

        var resumo = await new AnalyticsRepository(db, new CaixaSaldoCalculator(new CaixaRepository(db)))
            .GetResumoDiaAsync(empresaId);

        resumo.PixRecebidosHoje.Should().Be(2);
        resumo.ValorPixHoje.Should().Be(30m);
    }

    [SkippableFact]
    public async Task Sem_pagamentos_retorna_contagem_e_valor_zero()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        await using var db = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa { Id = empresaId, Nome = "Sem Pix", Documento = empresaId.ToString("N")[..14] });
        await db.SaveChangesAsync();

        var resumo = await new AnalyticsRepository(db, new CaixaSaldoCalculator(new CaixaRepository(db)))
            .GetResumoDiaAsync(empresaId);

        resumo.PixRecebidosHoje.Should().Be(0);
        resumo.ValorPixHoje.Should().Be(0m);
    }
}
