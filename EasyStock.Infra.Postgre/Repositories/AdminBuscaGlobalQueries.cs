using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementação Postgre da busca global Admin (F7). Usa ILIKE (pg) sobre
/// Empresas/Lojas/Usuários e resolve o nome do cliente de cada usuário.
/// </summary>
public sealed class AdminBuscaGlobalQueries(EasyStockDbContext db) : IAdminBuscaGlobalQueries
{
    public async Task<AdminBuscaGlobalResultado> BuscarAsync(
        string padrao, string? padraoDigitos, int limite, CancellationToken ct = default)
    {
        // ─── Clientes (Empresa) ───
        var clientes = await db.Empresas.AsNoTracking()
            .Where(e => EF.Functions.ILike(e.Nome, padrao)
                        || (e.Documento != null && EF.Functions.ILike(e.Documento, padrao))
                        || (padraoDigitos != null && e.Documento != null && EF.Functions.ILike(e.Documento, padraoDigitos)))
            .OrderBy(e => e.Nome)
            .Take(limite)
            .Select(e => new BuscaClienteRow(e.Id, e.Nome, e.Documento))
            .ToListAsync(ct);

        // ─── Lojas (cross-tenant) ───
        var lojas = await db.Lojas.AsNoTracking()
            .Where(l => EF.Functions.ILike(l.Nome, padrao))
            .OrderBy(l => l.Nome)
            .Take(limite)
            .Join(db.Empresas, l => l.EmpresaId, e => e.Id,
                (l, e) => new BuscaLojaRow(l.Id, l.Nome, e.Id, e.Nome, l.Ativa))
            .ToListAsync(ct);

        // ─── Usuários (cross-tenant) ───
        var usuariosRaw = await db.Usuarios.AsNoTracking()
            .Where(u => EF.Functions.ILike(u.Nome, padrao) || EF.Functions.ILike(u.Email, padrao))
            .OrderBy(u => u.Nome)
            .Take(limite)
            .Select(u => new
            {
                u.Id,
                u.Nome,
                u.Email,
                u.Ativo,
                EmpresaId = db.UsuariosEmpresas
                    .Where(ue => ue.UsuarioId == u.Id)
                    .Select(ue => (Guid?)ue.EmpresaId)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var empresaIds = usuariosRaw.Where(u => u.EmpresaId.HasValue).Select(u => u.EmpresaId!.Value).Distinct().ToList();
        var empresaNomes = empresaIds.Count > 0
            ? await db.Empresas.AsNoTracking()
                .Where(e => empresaIds.Contains(e.Id))
                .Select(e => new { e.Id, e.Nome })
                .ToDictionaryAsync(e => e.Id, e => e.Nome, ct)
            : new();

        var usuarios = usuariosRaw.Select(u => new BuscaUsuarioRow(
            u.Id,
            u.Nome,
            u.Email,
            u.Ativo,
            u.EmpresaId,
            u.EmpresaId.HasValue && empresaNomes.TryGetValue(u.EmpresaId.Value, out var en) ? en : null))
            .ToList();

        return new AdminBuscaGlobalResultado(clientes, lojas, usuarios);
    }
}
