using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Services.Helpdesk;

public sealed record SlaResolvido(int MinutosResposta, int MinutosResolucao, DateTime PrazoResposta, DateTime PrazoResolucao);

/// <summary>
/// Resolve a configuracao de SLA aplicavel a uma empresa+prioridade. Hierarquia:
/// (1) Override por empresa, (2) por plano da assinatura ativa, (3) default global.
/// Retorna sempre uma resolucao — fallback final usa 8h/24h se nada estiver configurado.
/// </summary>
public sealed class SlaResolver(EasyStockDbContext db)
{
    public async Task<SlaResolvido> ResolverAsync(Guid empresaId, TicketPrioridade prioridade, DateTime? referencia = null, CancellationToken ct = default)
    {
        var agora = referencia ?? DateTime.UtcNow;

        // 1. Override por empresa
        var porEmpresa = await db.SlaConfiguracoes
            .AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Prioridade == prioridade)
            .FirstOrDefaultAsync(ct);
        if (porEmpresa is not null)
            return Build(porEmpresa, agora);

        // 2. Por plano (resolve via assinatura ativa)
        var planoId = await db.AssinaturasEmpresa
            .AsNoTracking()
            .Where(a => a.EmpresaId == empresaId && a.Status == StatusAssinatura.Ativa)
            .OrderByDescending(a => a.DataInicio)
            .Select(a => (Guid?)a.PlanoId)
            .FirstOrDefaultAsync(ct);

        if (planoId.HasValue)
        {
            var porPlano = await db.SlaConfiguracoes
                .AsNoTracking()
                .Where(s => s.PlanoId == planoId.Value && s.Prioridade == prioridade)
                .FirstOrDefaultAsync(ct);
            if (porPlano is not null)
                return Build(porPlano, agora);
        }

        // 3. Default global (EmpresaId=NULL e PlanoId=NULL)
        var global = await db.SlaConfiguracoes
            .AsNoTracking()
            .Where(s => s.EmpresaId == null && s.PlanoId == null && s.Prioridade == prioridade)
            .FirstOrDefaultAsync(ct);
        if (global is not null)
            return Build(global, agora);

        // 4. Fallback final hardcoded — defesa contra DB vazio.
        var (resp, resol) = prioridade switch
        {
            TicketPrioridade.Critica => (30, 240),
            TicketPrioridade.Alta => (120, 480),
            TicketPrioridade.Normal => (480, 1440),
            TicketPrioridade.Baixa => (1440, 4320),
            _ => (480, 1440)
        };
        return new SlaResolvido(resp, resol, agora.AddMinutes(resp), agora.AddMinutes(resol));
    }

    private static SlaResolvido Build(SlaConfiguracao s, DateTime agora) =>
        new(s.MinutosResposta, s.MinutosResolucao, agora.AddMinutes(s.MinutosResposta), agora.AddMinutes(s.MinutosResolucao));
}
