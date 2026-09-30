using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

/// <summary>
/// Usuários da empresa com os perfis que têm nela (S41). O vínculo pode apontar para um perfil padrão
/// (<c>EmpresaId</c> nulo), que o filtro de tenant e a RLS escondem: por isso a leitura roda com bypass
/// e sem filtros globais, e o <c>empresaId</c> vai explícito no WHERE de cada vínculo (ADR-0010).
/// </summary>
public sealed class AtendenteRepository(EasyStockDbContext db) : IAtendenteRepository
{
    public Task<IReadOnlyList<UsuarioDaEmpresa>> ListarUsuariosAtivosAsync(Guid empresaId, CancellationToken ct = default) =>
        ListarAsync(empresaId, usuarioId: null, ct);

    public async Task<UsuarioDaEmpresa?> ObterUsuarioAtivoAsync(Guid empresaId, Guid usuarioId, CancellationToken ct = default) =>
        (await ListarAsync(empresaId, usuarioId, ct)).SingleOrDefault();

    private async Task<IReadOnlyList<UsuarioDaEmpresa>> ListarAsync(Guid empresaId, Guid? usuarioId, CancellationToken ct)
    {
        using var _ = db.UseRowLevelSecurityBypass();

        var usuarios = await db.UsuariosEmpresas
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(ue => ue.EmpresaId == empresaId && ue.Ativo && ue.Usuario!.Ativo)
            .Where(ue => usuarioId == null || ue.UsuarioId == usuarioId)
            .Select(ue => new { ue.Usuario!.Id, ue.Usuario.Nome, ue.Usuario.Email })
            .ToListAsync(ct);
        if (usuarios.Count == 0) return [];

        var ids = usuarios.Select(u => u.Id).ToList();
        var perfis = await db.UsuariosPerfis
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(up => up.EmpresaId == empresaId && ids.Contains(up.UsuarioId))
            .Select(up => new
            {
                up.UsuarioId,
                up.Perfil!.Nivel,
                Permissoes = up.Perfil.Permissoes.Select(p => p.Permissao).ToList(),
            })
            .ToListAsync(ct);

        return usuarios
            .Select(u => new UsuarioDaEmpresa(u.Id, u.Nome, u.Email, perfis
                .Where(p => p.UsuarioId == u.Id)
                .Select(p => new PerfilNaEmpresa(p.Nivel, p.Permissoes.Distinct().ToList()))
                .ToList()))
            .ToList();
    }
}
