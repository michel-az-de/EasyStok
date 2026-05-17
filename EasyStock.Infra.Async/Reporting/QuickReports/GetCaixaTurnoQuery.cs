using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.QuickReports;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.QuickReports;

/// <summary>
/// Quick Report: caixa-turno — resumo do caixa do dia para o painel mobile.
/// Agrega <see cref="EasyStock.Domain.Entities.MovimentoCaixa"/> ativos do dia
/// mais as vendas registradas. Síncrono, &lt; 1s, sem paginação (§27.7).
/// </summary>
public sealed class GetCaixaTurnoQuery(
    EasyStockDbContext    db,
    ICurrentUserAccessor  currentUser)
{
    public async Task<CaixaTurnoDto> ExecuteAsync(Guid? lojaId, CancellationToken ct)
    {
        var empresaId = currentUser.EmpresaId;
        var hoje      = DateTime.UtcNow.Date;
        var amanha    = hoje.AddDays(1);

        var movQuery = db.MovimentosCaixa
            .AsNoTracking()
            .Where(m => m.EmpresaId == empresaId
                     && m.DataMovimento >= hoje
                     && m.DataMovimento < amanha
                     && m.EstornadoEm == null);   // somente ativos

        if (lojaId.HasValue)
            movQuery = movQuery.Where(m => m.LojaId == lojaId.Value);

        var movimentos = await movQuery
            .Select(m => new { m.Tipo, m.Valor, m.RegistradoPorNome })
            .ToListAsync(ct);

        // Entradas = tipo "entrada" ou "abertura" (saldo inicial do caixa)
        var totalEntradas = movimentos
            .Where(m => m.Tipo is "entrada" or "abertura")
            .Sum(m => m.Valor);

        // Saídas = tipo "saida" (despesas, sangrias)
        var totalSaidas = movimentos
            .Where(m => m.Tipo == "saida")
            .Sum(m => m.Valor);

        // Vendas do dia adicionadas ao total de entradas
        var totalVendas = await db.Vendas
            .AsNoTracking()
            .Where(v => v.EmpresaId == empresaId
                     && v.DataVenda >= hoje
                     && v.DataVenda < amanha
                     && (lojaId == null || v.LojaId == lojaId))
            .SumAsync(v => (decimal?)v.ValorTotal.Valor, ct) ?? 0m;

        totalEntradas += totalVendas;

        var saldoAtual = totalEntradas - totalSaidas;

        // Último operador que registrou movimento hoje
        var operador = movimentos
            .LastOrDefault(m => m.RegistradoPorNome != null)
            ?.RegistradoPorNome;

        return new CaixaTurnoDto(
            TotalEntradas: Math.Round(totalEntradas, 2),
            TotalSaidas:   Math.Round(totalSaidas,   2),
            TotalVendas:   Math.Round(totalVendas,   2),
            SaldoAtual:    Math.Round(saldoAtual,    2),
            Operador:      operador);
    }
}
