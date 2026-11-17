using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2813: o entregue importado do histórico entra com a marca de quitado, sem pagamento, caixa nem conta a receber.
/// Os leitores que tiram "pendente" de total menos pago não podem tratá-lo como devedor: o dashboard (consulta traduzida
/// para SQL) e o impresso. O pedido entregue sem a marca e sem pagamento segue pendente nos dois.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class QuitadoDoHistoricoNosLeitoresTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Dashboard_nao_lista_como_pendente_o_entregue_quitado_no_historico()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresa, quitado, semMarca) = await GravarParDeEntreguesAsync();

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresa);
        var repo = new AnalyticsRepository(leitura, new CaixaSaldoCalculator(new CaixaRepository(leitura)));
        var pendentes = await repo.GetPedidosPendentesAsync(empresa, 30);

        pendentes.Select(p => p.Id).Should().NotContain(quitado, "o histórico já foi pago na origem: não há saldo em aberto")
            .And.Contain(semMarca, "sem a marca o entregue sem pagamento segue pendente");
        pendentes.Single(p => p.Id == semMarca).EmAberto.Should().Be(45m);
    }

    [SkippableFact]
    public async Task Impresso_sai_como_pago_para_o_entregue_quitado_no_historico_e_nao_pago_para_o_sem_marca()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresa, quitado, semMarca) = await GravarParDeEntreguesAsync();

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresa);
        var montar = new MontarPedidoImpressoUseCase(new PedidoImpressoQueries(leitura), TimeProvider.System);

        var doHistorico = await montar.ExecuteAsync(new MontarPedidoImpressoInput(empresa, quitado));
        var comum = await montar.ExecuteAsync(new MontarPedidoImpressoInput(empresa, semMarca));

        doHistorico!.Cobranca.Pago.Should().BeTrue("a reimpressão do histórico não pode sair cobrando de novo");
        doHistorico.Cobranca.Recebido.Should().Be(0m, "a marca quita o pedido, mas não inventa pagamento");
        comum!.Cobranca.Pago.Should().BeFalse("sem a marca o entregue sem pagamento segue a receber");
    }

    private async Task<(Guid Empresa, Guid Quitado, Guid SemMarca)> GravarParDeEntreguesAsync()
    {
        var empresa = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        db.Empresas.Add(new Empresa
        {
            Id = empresa,
            Nome = "Empresa Historico",
            Documento = empresa.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        });
        var quitado = Entregue(empresa, 80m);
        quitado.MarcarQuitadoHistorico(DateTime.UtcNow);
        var semMarca = Entregue(empresa, 45m);
        db.Pedidos.AddRange(quitado, semMarca);
        await db.SaveChangesAsync();
        return (empresa, quitado.Id, semMarca.Id);
    }

    private static Pedido Entregue(Guid empresaId, decimal total)
    {
        var pedido = Pedido.Criar(empresaId);
        pedido.Status = "entregue";
        pedido.Total = Dinheiro.FromDecimal(total);
        return pedido;
    }
}
