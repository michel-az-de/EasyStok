using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Atendimento.Ocorrencias;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Pagamentos;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// F14 (#1244), lacuna 3, em Postgres real: resolver a ocorrência com reembolso grava a saída no
/// caixa do dia (origem "ocorrencia") no mesmo commit, e o <c>GET api/caixa/dia</c> já a mostra.
/// </summary>
public class ReembolsoNoCaixaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    // 12h em Brasília: o dia civil do caixa é 30/09 nos dois fusos.
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Dia = new(2026, 9, 30);

    private sealed class RelogioFixo(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private static async Task<(Pedido Pedido, Ocorrencia Ocorrencia)> CenarioAsync(EasyStockDbContext db, Guid empresaId, bool pagoOnline)
    {
        var empresa = Empresa.Criar($"Empresa {empresaId:N}", null);
        empresa.Id = empresaId;
        db.Empresas.Add(empresa);
        var pedido = Pedido.Criar(empresaId, origem: "whatsapp");
        db.Pedidos.Add(pedido);
        // Caixa aberto às 9h de Brasília com R$ 100.
        db.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, Agora.AddHours(-3)));
        if (pagoOnline)
        {
            var cobranca = CobrancaPedido.CriarOnline(empresaId, pedido.Id, 80m, "pref-1", "https://mp/link",
                Agora.AddHours(1), 1, Agora.AddMinutes(-30));
            cobranca.MarcarPaga("pay-1", 80m, "credito", Agora.AddMinutes(-20));
            db.Set<CobrancaPedido>().Add(cobranca);
        }
        var ocorrencia = Ocorrencia.Abrir(empresaId, pedido.Id, Guid.NewGuid(), null, OrigemOcorrencia.Dona,
            CategoriaOcorrencia.ProdutoImproprio, "bolo chegou azedo", Agora.AddMinutes(-5));
        db.Ocorrencias.Add(ocorrencia);
        await db.SaveChangesAsync();
        return (pedido, ocorrencia);
    }

    private static ResolverOcorrenciaUseCase Resolver(EasyStockDbContext db, IEstornoPedidoGateway gateway)
    {
        var cobrancas = new CobrancaPedidoRepository(db);
        var reembolsar = new ReembolsarPedidoUseCase(cobrancas, gateway,
            Substitute.For<IClienteCrmRepository>(), Substitute.For<INotificadorService>());
        return new ResolverOcorrenciaUseCase(new OcorrenciaRepository(db), reembolsar,
            new LancarReembolsoNoCaixaUseCase(new CaixaRepository(db), cobrancas), db, new RelogioFixo(Agora));
    }

    [SkippableFact]
    public async Task ReembolsoEfetuado_ViraSaidaNoCaixaDoDia_UmaVezSo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var (_, ocorrencia) = await CenarioAsync(db, empresaId, pagoOnline: true);
        var gateway = Substitute.For<IEstornoPedidoGateway>();
        gateway.EstornarAsync("pay-1", 30m, ocorrencia.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Ok("ref-1"));

        var r = await Resolver(db, gateway).ExecuteAsync(new ResolverOcorrenciaInput(
            empresaId, ocorrencia.Id, Guid.NewGuid(), "devolvido parte", Reembolsar: true, Valor: 30m));
        r!.Reembolso!.Situacao.Should().Be(SituacaoReembolso.Efetuado);

        // Chamar de novo para a mesma ocorrência não duplica a saída (idempotente por ocorrência).
        await new LancarReembolsoNoCaixaUseCase(new CaixaRepository(db), new CobrancaPedidoRepository(db))
            .ExecuteAsync(new LancarReembolsoNoCaixaInput(ocorrencia, r.Reembolso, null, Agora));
        await db.SaveChangesAsync();

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        var dia = await new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(new CaixaRepository(leitura)))
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, Dia));

        var saida = dia.Movimentos.Should().ContainSingle(m => m.Tipo == "saida").Subject;
        saida.Valor.Should().Be(30m);
        saida.Metodo.Should().Be("credito");
        saida.Origem.Should().Be(LancarReembolsoNoCaixaUseCase.Origem);
        saida.Referencia.Should().Be(ocorrencia.Id.ToString());
        dia.TotalSaidasExtras.Should().Be(30m);
        dia.SaldoAtendimento.Should().Be(70m, "100 de abertura menos 30 devolvidos");
        dia.PorMetodo.Should().ContainSingle(m => m.Metodo == "credito" && m.Valor == -30m);
    }

    [SkippableFact]
    public async Task ReembolsoManual_ViraSaidaNoMetodoOutro()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var (_, ocorrencia) = await CenarioAsync(db, empresaId, pagoOnline: false);

        var r = await Resolver(db, Substitute.For<IEstornoPedidoGateway>()).ExecuteAsync(new ResolverOcorrenciaInput(
            empresaId, ocorrencia.Id, Guid.NewGuid(), "devolvido em dinheiro", Reembolsar: true, Valor: 25m));
        r!.Reembolso!.Situacao.Should().Be(SituacaoReembolso.ManualNecessario);

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        var dia = await new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(new CaixaRepository(leitura)))
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, Dia));

        dia.Movimentos.Should().ContainSingle(m => m.Tipo == "saida" && m.Valor == 25m && m.Metodo == "outro");
        dia.SaldoAtendimento.Should().Be(75m);
    }
}
