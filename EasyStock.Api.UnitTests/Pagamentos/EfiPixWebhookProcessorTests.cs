using EasyStock.Api.UnitTests.Helpers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>
/// #1598: um webhook Pix da Efi traz vários txid e cada um grava sozinho. O txid que falha depois de mexer
/// na parcela deixa a baixa pela metade no contexto, e a gravação do txid seguinte a levava junto.
/// </summary>
public class EfiPixWebhookProcessorTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static ParcelaReceber NovaParcela()
    {
        var conta = ContaReceber.Criar(Empresa, clienteId: null, Guid.NewGuid(), "Cliente", DateTime.UtcNow);
        conta.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        conta.Emitir();
        var parcela = conta.Parcelas.First();
        parcela.ContaReceber = conta;
        return parcela;
    }

    [Fact]
    public async Task Txid_que_falha_e_descartado_antes_de_o_seguinte_gravar()
    {
        var quebrada = NovaParcela();
        var boa = NovaParcela();
        var contas = Substitute.For<IContaReceberRepository>();
        contas.GetParcelaByEfiTxidAsync("cr-quebrada", Arg.Any<CancellationToken>()).Returns(quebrada);
        contas.GetParcelaByEfiTxidAsync("cr-boa", Arg.Any<CancellationToken>()).Returns(boa);
        // Falha depois de a parcela já ter recebido o pagamento e o movimento de caixa ter sido adicionado.
        contas.UpdateAsync(quebrada.ContaReceber!, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("banco fora"));

        var bypass = Substitute.For<IRowLevelSecurityBypass>();
        var passos = new List<string>();
        var uow = Substitute.For<IUnitOfWork>().SetupExecuteInTransactionSemRetry<ReconciliarPixResult>();
        uow.When(u => u.DescartarAlteracoesPendentes()).Do(_ => passos.Add("descartar"));
        uow.CommitAsync().Returns(_ => { passos.Add("gravar"); return Task.FromResult(1); });

        var reconciliar = new ReconciliarPixParcelaReceberUseCase(contas, Substitute.For<ICaixaRepository>(),
            Substitute.For<IEfiPixService>(), uow, NullLogger<ReconciliarPixParcelaReceberUseCase>.Instance);
        var processor = new EfiPixWebhookProcessor(reconciliar, uow, bypass, NullLogger<EfiPixWebhookProcessor>.Instance);

        var act = () => processor.ProcessarAsync(
            """{"pix":[{"txid":"cr-quebrada","valor":"100.00"},{"txid":"cr-boa","valor":"100.00"}]}""",
            new Dictionary<string, string?>());

        // A falha continua subindo: a Efi só reenvia se não receber 200 (#787).
        await act.Should().ThrowAsync<InvalidOperationException>();
        passos.Should().Equal("descartar", "gravar");
    }

    [Fact]
    public async Task Lote_sem_falha_nao_descarta_nada()
    {
        var boa = NovaParcela();
        var contas = Substitute.For<IContaReceberRepository>();
        contas.GetParcelaByEfiTxidAsync("cr-boa", Arg.Any<CancellationToken>()).Returns(boa);
        var uow = Substitute.For<IUnitOfWork>().SetupExecuteInTransactionSemRetry<ReconciliarPixResult>();
        var bypass = Substitute.For<IRowLevelSecurityBypass>();
        var reconciliar = new ReconciliarPixParcelaReceberUseCase(contas, Substitute.For<ICaixaRepository>(),
            Substitute.For<IEfiPixService>(), uow, NullLogger<ReconciliarPixParcelaReceberUseCase>.Instance);
        var processor = new EfiPixWebhookProcessor(reconciliar, uow, bypass, NullLogger<EfiPixWebhookProcessor>.Instance);

        var r = await processor.ProcessarAsync("""{"pix":[{"txid":"cr-boa","valor":"100.00"}]}""",
            new Dictionary<string, string?>());

        r.Sucesso.Should().BeTrue();
        uow.DidNotReceive().DescartarAlteracoesPendentes();
        // #2743: o webhook e anonimo; sem o bypass a policy RLS zera a busca da parcela.
        bypass.Received(1).Begin();
        await uow.Received(1).CommitAsync();
    }
}
