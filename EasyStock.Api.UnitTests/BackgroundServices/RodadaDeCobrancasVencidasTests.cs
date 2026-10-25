using EasyStock.Api.BackgroundServices;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.BackgroundServices;

/// <summary>
/// #2048: cada cobrança da página tem sua tentativa, mesmo quando outras da mesma empresa falham.
/// </summary>
public class RodadaDeCobrancasVencidasTests
{
    private static CobrancaPedidoVencida Da(Guid loja) => new(Guid.NewGuid(), loja, Guid.NewGuid());

    [Fact]
    public async Task Fila_segue_com_loja_sem_credencial()
    {
        var presa = Guid.NewGuid();
        var saudavel = Guid.NewGuid();
        // A loja presa vem primeiro (cobranças mais antigas), com cinco itens; a saudável, depois.
        var vencidas = new[] { Da(presa), Da(presa), Da(presa), Da(presa), Da(presa), Da(saudavel), Da(saudavel) };
        var tentadas = new List<Guid>();

        var resumo = await RodadaDeCobrancasVencidas.ProcessarAsync(vencidas, (item, _) =>
        {
            tentadas.Add(item.EmpresaId);
            return item.EmpresaId == presa ? throw new HttpRequestException("credencial") : Task.CompletedTask;
        }, NullLogger.Instance, CancellationToken.None);

        tentadas.Count(l => l == saudavel).Should().Be(2, "a loja saudável é processada inteira");
        tentadas.Count(l => l == presa).Should().Be(5, "cada cobrança tem sua tentativa, mesmo na empresa com outras falhas");
        resumo.Should().Be(new RodadaDeCobrancasVencidas.Resumo(Processadas: 2, Falhas: 5, Puladas: 0));
    }

    [Fact]
    public async Task Tres_falhas_da_mesma_loja_nao_impedem_a_conciliacao_do_pedido_seguinte()
    {
        var empresa = Guid.NewGuid();
        var vencidas = new[] { Da(empresa), Da(empresa), Da(empresa), Da(empresa) };
        var pedidoComPagamentoAConferir = vencidas[^1];
        var tentadas = new List<Guid>();

        // A consulta do job devolve de novo as três cobranças antigas que falharam.
        // Sem avançar, um pagamento com webhook perdido nunca chega à consulta do provedor.
        for (var rodada = 0; rodada < 2; rodada++)
        {
            tentadas.Clear();
            var resumo = await RodadaDeCobrancasVencidas.ProcessarAsync(vencidas, (item, _) =>
            {
                tentadas.Add(item.CobrancaId);
                return item.CobrancaId == pedidoComPagamentoAConferir.CobrancaId
                    ? Task.CompletedTask
                    : throw new HttpRequestException("Falha persistente desta cobrança");
            }, NullLogger.Instance, CancellationToken.None);

            tentadas.Should().Contain(pedidoComPagamentoAConferir.CobrancaId,
                "três cobranças com falha não podem impedir a conciliação de outro pedido da mesma empresa");
            resumo.Should().Be(new RodadaDeCobrancasVencidas.Resumo(1, 3, 0));
        }
    }
    [Fact]
    public async Task Falhas_espalhadas_por_lojas_diferentes_nao_interrompem_nada()
    {
        var vencidas = Enumerable.Range(0, 5).Select(_ => Da(Guid.NewGuid())).ToArray();

        var resumo = await RodadaDeCobrancasVencidas.ProcessarAsync(vencidas,
            (_, _) => throw new InvalidOperationException("falhou"), NullLogger.Instance, CancellationToken.None);

        resumo.Should().Be(new RodadaDeCobrancasVencidas.Resumo(0, 5, 0));
    }

    [Fact]
    public async Task Timeout_de_uma_cobranca_conta_como_falha_e_a_rodada_segue()
    {
        // #2023: o HttpClient lança TaskCanceledException no timeout do Mercado Pago, com o token do job intacto.
        var vencidas = new[] { Da(Guid.NewGuid()), Da(Guid.NewGuid()) };
        var chamadas = 0;

        var resumo = await RodadaDeCobrancasVencidas.ProcessarAsync(vencidas, (_, _) =>
        {
            chamadas++;
            return chamadas == 1 ? throw new TaskCanceledException("timeout", new TimeoutException()) : Task.CompletedTask;
        }, NullLogger.Instance, CancellationToken.None);

        chamadas.Should().Be(2);
        resumo.Should().Be(new RodadaDeCobrancasVencidas.Resumo(Processadas: 1, Falhas: 1, Puladas: 0));
    }

    [Fact]
    public async Task Desligamento_no_meio_de_uma_cobranca_interrompe_a_rodada()
    {
        // #2023: o cancelamento do host que nasce dentro da cobrança não conta como falha dela. Na última cobrança não
        // há próxima volta do laço para relançar: engolido, a rodada terminaria como se nada tivesse acontecido.
        using var cts = new CancellationTokenSource();
        var vencidas = new[] { Da(Guid.NewGuid()) };
        var chamadas = 0;

        var act = () => RodadaDeCobrancasVencidas.ProcessarAsync(vencidas, (_, token) =>
        {
            chamadas++;
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, NullLogger.Instance, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        chamadas.Should().Be(1);
    }

    [Fact]
    public async Task Cancelamento_interrompe_a_rodada()
    {
        using var cts = new CancellationTokenSource();
        var vencidas = new[] { Da(Guid.NewGuid()), Da(Guid.NewGuid()) };
        var chamadas = 0;

        var act = () => RodadaDeCobrancasVencidas.ProcessarAsync(vencidas, (_, _) =>
        {
            chamadas++;
            cts.Cancel();
            return Task.CompletedTask;
        }, NullLogger.Instance, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        chamadas.Should().Be(1);
    }
}