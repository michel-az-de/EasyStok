using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Services;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

/// <summary>Implementa <see cref="IAudienciaUsuarios"/> (N4): leitura dos usuários internos no tenant do evento.</summary>
public sealed class AudienciaUsuarios(EasyStockDbContext db) : IAudienciaUsuarios
{
    public async Task<UsuarioParaAudiencia?> ObterAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var u = await db.Usuarios.AsNoTracking()
            .Where(x => x.Id == usuarioId)
            .Select(x => new { x.Id, x.Nome, x.Email, x.EmailConfirmado, x.Ativo, x.Telefone, x.TelefoneVerificadoEm, x.SenhaHash })
            .FirstOrDefaultAsync(ct);

        return u is null
            ? null
            : new UsuarioParaAudiencia(
                u.Id, u.Nome, u.Email, u.EmailConfirmado, u.Ativo, u.Telefone?.Value, u.TelefoneVerificadoEm,
                u.SenhaHash.StartsWith(Usuario.MarcadorDeConvite));
    }

    public async Task<IReadOnlyList<UsuarioParaAudiencia>> ListarDaEmpresaAsync(
        Guid empresaId, IReadOnlyCollection<NivelAcesso> niveis, CancellationToken ct = default)
    {
        // Sempre a empresa do evento: o vínculo ativo e o perfil são dela, e quem é SuperAdmin fica de fora.
        var usuarios = db.Usuarios.AsNoTracking()
            .Where(u => u.Ativo
                        && u.Empresas.Any(ue => ue.EmpresaId == empresaId && ue.Ativo)
                        && u.Perfis.Any(up => up.EmpresaId == empresaId && up.Perfil != null && niveis.Contains(up.Perfil.Nivel))
                        && !u.Perfis.Any(up => up.Perfil != null && up.Perfil.Nivel == NivelAcesso.SuperAdmin));

        var linhas = await usuarios
            .OrderBy(u => u.Nome)
            .Select(u => new { u.Id, u.Nome, u.Email, u.EmailConfirmado, u.Ativo, u.Telefone, u.TelefoneVerificadoEm })
            .ToListAsync(ct);

        return linhas
            .Select(u => new UsuarioParaAudiencia(
                u.Id, u.Nome, u.Email, u.EmailConfirmado, u.Ativo, u.Telefone?.Value, u.TelefoneVerificadoEm))
            .ToList();
    }

    public async Task<IReadOnlyList<UsuarioParaAudiencia>> ListarComPermissaoAsync(
        Guid empresaId, Permissao permissao, CancellationToken ct = default)
    {
        var linhas = await db.Usuarios.AsNoTracking()
            .Where(u => u.Ativo
                        && u.Empresas.Any(ue => ue.EmpresaId == empresaId && ue.Ativo)
                        && !u.Perfis.Any(up => up.Perfil != null && up.Perfil.Nivel == NivelAcesso.SuperAdmin))
            .OrderBy(u => u.Nome)
            .Select(u => new
            {
                u.Id, u.Nome, u.Email, u.EmailConfirmado, u.Ativo, u.Telefone, u.TelefoneVerificadoEm,
                Perfis = u.Perfis.Where(up => up.EmpresaId == empresaId && up.Perfil != null)
                    .Select(up => new
                    {
                        up.Perfil!.Nivel,
                        up.Perfil.PermissoesExplicitas,
                        Permissoes = up.Perfil.Permissoes.Select(p => p.Permissao).ToList(),
                    })
                    .ToList(),
            })
            .ToListAsync(ct);

        // A regra de permissão é uma só (PoliticaPermissao), a mesma do JWT; por isso o filtro fino é aqui, em memória.
        return linhas
            .Where(u => u.Perfis.Any(p => PoliticaPermissao.Tem(p.Nivel, p.Permissoes, permissao, p.PermissoesExplicitas)))
            .Select(u => new UsuarioParaAudiencia(
                u.Id, u.Nome, u.Email, u.EmailConfirmado, u.Ativo, u.Telefone?.Value, u.TelefoneVerificadoEm))
            .ToList();
    }
}
