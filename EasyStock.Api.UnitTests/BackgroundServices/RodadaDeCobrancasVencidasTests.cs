using EasyStock.Api.BackgroundServices;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.BackgroundServices;

/// <summary>
/// I3.2 (#1688): a falha da rodada do <c>CobrancaPedidoJob</c> conta por loja. Antes, três falhas quaisquer
/// interrompiam a rodada, e três cobranças presas de uma loja travavam a fila das demais.
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
        tentadas.Count(l => l == presa).Should().Be(RodadaDeCobrancasVencidas.FalhasParaPularALoja, "depois de três falhas o resto da loja fica para a próxima rodada");
        resumo.Should().Be(new RodadaDeCobrancasVencidas.Resumo(Processadas: 2, Falhas: 3, Puladas: 2));
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