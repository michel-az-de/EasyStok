using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.QuickReports;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.QuickReports;

/// <summary>
/// Quick Report: vendas-vendedor-turno — ranking de vendas por vendedor no dia atual.
/// Útil para o app do vendedor ("como estou hoje?") e para o gerente de loja.
/// Síncrono, &lt; 1s, sem paginação (§27.7).
/// </summary>
public sealed class GetVendasVendedorTurnoQuery(
    EasyStockDbContext   db,
    ICurrentUserAccessor currentUser)
{
    public async Task<VendasVendedorTurnoDto> ExecuteAsync(Guid? lojaId, CancellationToken ct)
    {
        var empresaId = currentUser.EmpresaId;
        var hoje      = DateTime.UtcNow.Date;
        var amanha    = hoje.AddDays(1);

        var query = db.Vendas
            .AsNoTracking()
            .Where(v => v.EmpresaId == empresaId
                     && v.DataVenda >= hoje
                     && v.DataVenda <  amanha);

        if (lojaId.HasValue)
            query = query.Where(v => v.LojaId == lojaId.Value);

        var porVendedor = await query
            .GroupBy(v => new
            {
                v.VendedorId,
                VendedorNome = v.Vendedor != null ? v.Vendedor.Nome : null,
            })
            .Select(g => new
            {
                g.Key.VendedorId,
                g.Key.VendedorNome,
                QtdVendas    = g.Count(),
                TotalVendido = g.Sum(v => (decimal?)v.ValorTotal.Valor) ?? 0m,
            })
            .OrderByDescending(x => x.TotalVendido)
            .ToListAsync(ct);

        var totalGeral      = porVendedor.Sum(x => x.TotalVendido);
        var qtdVendasGeral  = porVendedor.Sum(x => x.QtdVendas);

        var vendedores = porVendedor
            .Select((x, idx) => new VendedorTurnoDto(
                VendedorId:   x.VendedorId,
                VendedorNome: x.VendedorNome ?? "Sem vendedor",
                QtdVendas:    x.QtdVendas,
                TotalVendido: Math.Round(x.TotalVendido, 2),
                Ranking:      idx + 1))
            .ToList();

        return new VendasVendedorTurnoDto(
            Vendedores:    vendedores,
            TotalGeral:    Math.Round(totalGeral, 2),
            QtdVendasGeral: qtdVendasGeral);
    }
}
