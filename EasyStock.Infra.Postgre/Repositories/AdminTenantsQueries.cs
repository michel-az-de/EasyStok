using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class AdminTenantsQueries(EasyStockDbContext db) : IAdminTenantsQueries
{
    public async Task<(IReadOnlyList<TenantListItem> Items, int Total)> ListarAsync(
        int page, int pageSize, string? search, StatusAssinatura? status)
    {
        var query = db.Empresas.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e =>
                EF.Functions.ILike(e.Nome, $"%{search}%") ||
                (e.Documento != null && EF.Functions.ILike(e.Documento, $"%{search}%")));

        if (status.HasValue)
            query = query.Where(e => db.AssinaturasEmpresa.Any(a => a.EmpresaId == e.Id && a.Status == status.Value));

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(e => e.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new TenantListItem(
                e.Id,
                e.Nome,
                e.Documento,
                e.CriadoEm,
                db.UsuariosEmpresas.Count(ue => ue.EmpresaId == e.Id),
                db.Lojas.Count(l => l.EmpresaId == e.Id),
                db.AssinaturasEmpresa
                    .Where(a => a.EmpresaId == e.Id)
                    .OrderByDescending(a => a.DataInicio)
                    .Select(a => a.Plano != null ? a.Plano.Nome : null)
                    .FirstOrDefault(),
                db.AssinaturasEmpresa
                    .Where(a => a.EmpresaId == e.Id)
                    .OrderByDescending(a => a.DataInicio)
                    .Select(a => (StatusAssinatura?)a.Status)
                    .FirstOrDefault(),
                db.AssinaturasEmpresa
                    .Where(a => a.EmpresaId == e.Id)
                    .OrderByDescending(a => a.DataInicio)
                    .Select(a => a.DataFim)
                    .FirstOrDefault()))
            .ToListAsync();

        return (items, total);
    }

    public async Task<IReadOnlyList<TenantExportRow>> ListarParaExportarAsync(
        TenantExportFiltro filtro, CancellationToken ct = default)
    {
        var query = db.Empresas.AsNoTracking().AsQueryable();

        // Ids (selecionados na tela) tem precedencia sobre os filtros de busca.
        if (filtro.Ids is { Count: > 0 })
        {
            var ids = filtro.Ids;
            query = query.Where(e => ids.Contains(e.Id));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(filtro.Search))
                query = query.Where(e =>
                    EF.Functions.ILike(e.Nome, $"%{filtro.Search}%") ||
                    (e.Documento != null && EF.Functions.ILike(e.Documento, $"%{filtro.Search}%")));

            if (filtro.Status.HasValue)
                query = query.Where(e => db.AssinaturasEmpresa.Any(a => a.EmpresaId == e.Id && a.Status == filtro.Status.Value));
        }

        var limite = Math.Clamp(filtro.Limite, 1, 10000);

        // Ordenacao deterministica (igual a listagem + Id de desempate) — sem isso o
        // Take(10k) e "exportar selecionados" voltariam em ordem arbitraria do banco.
        return await query
            .OrderByDescending(e => e.CriadoEm)
            .ThenBy(e => e.Id)
            .Take(limite)
            .Select(e => new TenantExportRow(
                e.Nome,
                e.Documento,
                db.AssinaturasEmpresa
                    .Where(a => a.EmpresaId == e.Id)
                    .OrderByDescending(a => a.DataInicio)
                    .Select(a => a.Plano != null ? a.Plano.Nome : null)
                    .FirstOrDefault(),
                db.AssinaturasEmpresa
                    .Where(a => a.EmpresaId == e.Id)
                    .OrderByDescending(a => a.DataInicio)
                    .Select(a => a.Plano != null ? (decimal?)a.Plano.PrecoMensal : null)
                    .FirstOrDefault(),
                db.AssinaturasEmpresa
                    .Where(a => a.EmpresaId == e.Id)
                    .OrderByDescending(a => a.DataInicio)
                    .Select(a => (StatusAssinatura?)a.Status)
                    .FirstOrDefault(),
                db.UsuariosEmpresas.Count(ue => ue.EmpresaId == e.Id),
                db.Lojas.Count(l => l.EmpresaId == e.Id),
                e.CriadoEm,
                db.AssinaturasEmpresa
                    .Where(a => a.EmpresaId == e.Id)
                    .OrderByDescending(a => a.DataInicio)
                    .Select(a => a.DataFim)
                    .FirstOrDefault()))
            .ToListAsync(ct);
    }

    public async Task<TenantDetail?> ObterDetalheAsync(Guid empresaId)
    {
        var empresa = await db.Empresas.AsNoTracking().FirstOrDefaultAsync(e => e.Id == empresaId);
        if (empresa is null) return null;

        var assinatura = await db.AssinaturasEmpresa.AsNoTracking()
            .Include(a => a.Plano)
            .Where(a => a.EmpresaId == empresaId)
            .OrderByDescending(a => a.DataInicio)
            .FirstOrDefaultAsync();

        var lojas = await db.Lojas.AsNoTracking()
            .Where(l => l.EmpresaId == empresaId)
            .OrderByDescending(l => l.Ativa)
            .ThenBy(l => l.Nome)
            .Select(l => new TenantLojaInfo(
                l.Id, l.Nome, l.Descricao, l.Documento, l.Endereco, l.Telefone,
                l.Ativa, l.CriadoEm, l.AlteradoEm))
            .ToListAsync();

        var usuarios = await db.UsuariosEmpresas.AsNoTracking()
            .Include(ue => ue.Usuario)
            .Where(ue => ue.EmpresaId == empresaId)
            .Select(ue => new TenantUsuarioInfo(
                ue.Usuario!.Id,
                ue.Usuario.Nome,
                ue.Usuario.Email,
                ue.Usuario.Ativo,
                ue.Usuario.UltimoAcessoEm,
                db.UsuariosPerfis
                    .Where(up => up.UsuarioId == ue.UsuarioId && up.EmpresaId == empresaId)
                    .Select(up => up.Perfil != null ? (NivelAcesso?)up.Perfil.Nivel : null)
                    .FirstOrDefault()))
            .ToListAsync();

        var usuariosIds = usuarios.Select(u => u.Id).ToList();
        var auditLogs = await db.AuditLogs.AsNoTracking()
            .Where(a => usuariosIds.Contains(a.UsuarioId))
            .OrderByDescending(a => a.DataHora)
            .Take(20)
            .Select(a => new TenantAuditLogInfo(a.Id, a.UsuarioId, a.Acao, a.Sucesso, a.Detalhes, a.Ip, a.DataHora))
            .ToListAsync();

        TenantAssinaturaInfo? assinaturaInfo = assinatura is null ? null : new TenantAssinaturaInfo(
            assinatura.Id,
            assinatura.Status,
            assinatura.DataInicio,
            assinatura.DataFim,
            assinatura.TrialFim,
            assinatura.TrialAtivo,
            // ADM-004 (#694): trial vencido sem plano pago vigente — espelha
            // SubscriptionGateMiddleware.TrialExpiradoSemPlanoAtivo. Status efetivo p/ o Admin.
            assinatura.Status == StatusAssinatura.Ativa
                && assinatura.TrialFim.HasValue && assinatura.TrialFim.Value < DateTime.UtcNow
                && !(assinatura.DataFim.HasValue && assinatura.DataFim.Value >= DateTime.UtcNow),
            assinatura.CupomCodigo,
            assinatura.DescontoAplicado,
            assinatura.Plano is null ? null : new TenantPlanoInfo(
                assinatura.Plano.Id,
                assinatura.Plano.Nome,
                assinatura.Plano.PrecoMensal,
                assinatura.Plano.LimiteLojas,
                assinatura.Plano.LimiteUsuarios,
                assinatura.Plano.LimiteProdutos));

        return new TenantDetail(
            new TenantEmpresaInfo(empresa.Id, empresa.Nome, empresa.Documento, empresa.CriadoEm),
            assinaturaInfo,
            lojas,
            usuarios,
            auditLogs);
    }

    public async Task<(IReadOnlyList<TenantAuditLogInfo> Items, int Total)> GetAuditLogsPagedAsync(
        Guid empresaId, int page, int pageSize)
    {
        var usuariosIds = await db.UsuariosEmpresas.AsNoTracking()
            .Where(ue => ue.EmpresaId == empresaId)
            .Select(ue => ue.UsuarioId)
            .ToListAsync();

        var query = db.AuditLogs.AsNoTracking()
            .Where(a => usuariosIds.Contains(a.UsuarioId));

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(a => a.DataHora)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new TenantAuditLogInfo(a.Id, a.UsuarioId, a.Acao, a.Sucesso, a.Detalhes, a.Ip, a.DataHora))
            .ToListAsync();

        return (items, total);
    }
}
