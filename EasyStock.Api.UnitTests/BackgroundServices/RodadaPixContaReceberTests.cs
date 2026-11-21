using EasyStock.Api.BackgroundServices;
using EasyStock.Domain.Entities.Financeiro;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.BackgroundServices;

/// <summary>
/// #2580: a rodada para em 3 falhas SEGUIDAS, como diz o log. O contador cumulativo parava a rodada com o
/// gateway respondendo, e as parcelas seguintes esperavam mais 1 hora pela consulta.
/// </summary>
public class RodadaPixContaReceberTests
{
    private static ParcelaReceber ComPix(string txid)
    {
        var p = ParcelaReceber.Criar(Guid.NewGuid(), Guid.NewGuid(), 1, 100m, DateTime.UtcNow.AddDays(5));
        p.AssociarPix(txid, "copia", "qr", DateTime.UtcNow.AddHours(1));
        return p;
    }

    private static async Task<(RodadaPixContaReceber.Resumo Resumo, List<string> Tentadas)> Rodar(params string[] txids)
    {
        var tentadas = new List<string>();
        var resumo = await RodadaPixContaReceber.ProcessarAsync(txids.Select(ComPix).ToList(), (txid, _) =>
        {
            tentadas.Add(txid);
            return txid.StartsWith("falha") ? throw new HttpRequestException("gateway") : Task.FromResult(true);
        }, () => { }, NullLogger.Instance, CancellationToken.None);
        return (resumo, tentadas);
    }

    [Fact]
    public async Task Falhas_intercaladas_com_sucesso_nao_interrompem_a_rodada()
    {
        var (resumo, tentadas) = await Rodar("falha-1", "falha-2", "ok-1", "falha-3", "falha-4", "ok-2");

        tentadas.Should().HaveCount(6);
        resumo.Should().Be(new RodadaPixContaReceber.Resumo(Consultadas: 2, Reconciliadas: 2, Falhas: 4));
    }

    [Fact]
    public async Task Tres_falhas_seguidas_interrompem_a_rodada()
    {
        var (resumo, tentadas) = await Rodar("ok-1", "falha-1", "falha-2", "falha-3", "ok-2");

        tentadas.Should().Equal("ok-1", "falha-1", "falha-2", "falha-3");
        resumo.Should().Be(new RodadaPixContaReceber.Resumo(Consultadas: 1, Reconciliadas: 1, Falhas: 3));
    }
}
