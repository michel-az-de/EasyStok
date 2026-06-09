using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.QuickReports;
using EasyStock.Domain.Fiscal;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.QuickReports;

/// <summary>
/// Quick Report: nfce-hoje — resumo das NFC-e emitidas no dia atual.
/// Retorna contagens por status e taxa de sucesso (Autorizadas / Total emitidas).
/// Síncrono, &lt; 1s, sem paginação (§27.7).
/// </summary>
public sealed class GetNfceHojeQuery(
    EasyStockDbContext   db,
    ICurrentUserAccessor currentUser)
{
    // Pendentes = ainda em processo (não tiveram resultado final da SEFAZ)
    private static readonly StatusNfe[] _statusPendentes =
    [
        StatusNfe.Rascunho,
        StatusNfe.EnviadaAguardandoRetorno,
        StatusNfe.FalhaTransiente,
    ];

    public async Task<NfceHojeDto> ExecuteAsync(Guid? lojaId, CancellationToken ct)
    {
        var empresaId = currentUser.EmpresaId;
        var (hoje, amanha) = HorarioBrasil.JanelaDiaUtc();

        // Nota: NfeDocumento não possui LojaId — está vinculado à loja via PedidoId.
        // O filtro de lojaId não é aplicável nesta query; o parâmetro é mantido
        // para compatibilidade de contrato com os demais Quick Reports.
        _ = lojaId; // ignorado intencionalmente

        var query = db.NfeDocumentos
            .AsNoTracking()
            .Where(n => n.EmpresaId == empresaId
                     && n.CriadoEm >= hoje
                     && n.CriadoEm <  amanha);

        var contagens = await query
            .GroupBy(n => n.Status)
            .Select(g => new { Status = g.Key, Qtd = g.Count() })
            .ToListAsync(ct);

        var autorizadas = contagens.FirstOrDefault(c => c.Status == StatusNfe.Autorizada)?.Qtd  ?? 0;
        var canceladas  = contagens.FirstOrDefault(c => c.Status == StatusNfe.Cancelada)?.Qtd   ?? 0;
        var rejeitadas  = contagens.FirstOrDefault(c => c.Status == StatusNfe.Rejeitada)?.Qtd   ?? 0;

        var pendentes = contagens
            .Where(c => _statusPendentes.Contains(c.Status))
            .Sum(c => c.Qtd);

        // Sucesso = Autorizadas / (tudo exceto pendentes e inutilizadas)
        var totalFinalizados = autorizadas + canceladas + rejeitadas;
        var percentSucesso   = totalFinalizados > 0
            ? Math.Round((decimal)autorizadas / totalFinalizados * 100, 1)
            : 0m;

        return new NfceHojeDto(
            Autorizadas:   autorizadas,
            Canceladas:    canceladas,
            Rejeitadas:    rejeitadas,
            Pendentes:     pendentes,
            PercentSucesso: percentSucesso);
    }
}
