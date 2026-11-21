using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// #2736 (CXF-04): Pix acima do saldo da parcela. O crédito segue limitado ao saldo; o que passou fica registrado
/// na linha do tempo da conta, para quem concilia o banco achar o dinheiro que entrou e não está no Extrato.
/// </summary>
public class ReconciliarPixParcelaReceberExcedenteTests
{
    private const string Txid = "cr9999990123456789abcdef01";
    private static readonly Guid Empresa = Guid.NewGuid();

    private static (ReconciliarPixParcelaReceberUseCase Uc, List<ContaFinanceiraEvento> Eventos) Montar(decimal saldo)
    {
        var conta = ContaReceber.Criar(Empresa, null, Guid.NewGuid(), "Cliente", DateTime.UtcNow);
        conta.AdicionarParcela(1, saldo, DateTime.UtcNow.AddDays(30));
        conta.Emitir();
        var parcela = conta.Parcelas.First();
        parcela.ContaReceber = conta;
        parcela.AssociarPix(Txid, "copia-e-cola", "qr", DateTime.UtcNow.AddHours(1));

        var eventos = new List<ContaFinanceiraEvento>();
        var repo = Substitute.For<IContaReceberRepository>();
        repo.GetParcelaByEfiTxidAsync(Txid, Arg.Any<CancellationToken>()).Returns(parcela);
        repo.AddEventoAsync(Arg.Do<ContaFinanceiraEvento>(eventos.Add), Arg.Any<CancellationToken>());
        var uc = new ReconciliarPixParcelaReceberUseCase(repo, Substitute.For<ICaixaRepository>(),
            Substitute.For<IEfiPixService>(),
            Substitute.For<IUnitOfWork>().SetupExecuteInTransactionSemRetry<ReconciliarPixResult>(),
            Substitute.For<ILogger<ReconciliarPixParcelaReceberUseCase>>());
        return (uc, eventos);
    }

    [Fact]
    public async Task Pix_acima_do_saldo_registra_o_excedente_na_linha_do_tempo_da_conta()
    {
        var (uc, eventos) = Montar(saldo: 50m);

        var r = await uc.ExecuteAsync(new ReconciliarPixParcelaReceberCommand(Txid, 80m, DateTime.UtcNow));

        r.Reconciliado.Should().BeTrue();
        eventos.Should().Contain(e => e.TipoEvento == TipoEventoContaFinanceira.PixReconciliado
                                      && e.Descricao != null && e.Descricao.Contains("R$ 30,00") && e.Descricao.Contains("excedente"));
    }

    [Fact]
    public async Task Pix_no_valor_do_saldo_nao_registra_excedente()
    {
        var (uc, eventos) = Montar(saldo: 50m);

        await uc.ExecuteAsync(new ReconciliarPixParcelaReceberCommand(Txid, 50m, DateTime.UtcNow));

        eventos.Should().ContainSingle();
        eventos.Single().Descricao.Should().NotContain("excedente");
    }
}
